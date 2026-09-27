#!/usr/bin/env python3
"""Check F20 migration ownership, unique indexes and concurrent HTTP creates."""

import argparse
from concurrent.futures import ThreadPoolExecutor
import os
import subprocess
import threading

from http_probe_support import (
    cookie_opener,
    extract_antiforgery_token,
    request,
    submit_login,
    wait_until_ready,
)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-url", required=True)
    parser.add_argument("--pg-port", required=True)
    parser.add_argument("--pg-user", required=True)
    args = parser.parse_args()
    base = args.base_url.rstrip("/")

    def sql(database: str, statement: str, *, expect_failure: bool = False) -> str:
        completed = subprocess.run(
            ["psql", "-X", "-h", "127.0.0.1", "-p", args.pg_port,
             "-U", args.pg_user, "-d", database, "-v", "ON_ERROR_STOP=1",
             "-A", "-t", "-c", statement],
            capture_output=True, text=True, check=False,
        )
        if expect_failure:
            return str(completed.returncode != 0).lower()
        if completed.returncode != 0:
            raise RuntimeError(completed.stderr)
        return completed.stdout.strip()

    checks = {
        "empty_database_migrated": sql(
            "f20_empty", "SELECT COUNT(*) FROM \"__EFMigrationsHistory\""
        ) == "7",
        "prior_database_migrated": sql(
            "f20_prior", "SELECT COUNT(*) FROM \"__EFMigrationsHistory\""
        ) == "7",
        "old_owners_and_reserved_suffixes": (
            sql("f20_prior", 'SELECT "Id", "Slug" FROM "Posts" ORDER BY "Id"')
            == "2001|shared-post\n2002|shared-post-3\n2003|shared-post-2\n"
               "2004|post-2005\n2005|post-2004"
            and sql("f20_prior", 'SELECT "Id", "Slug" FROM "Categories" ORDER BY "Id"')
            == "1001|shared-category\n1002|shared-category-3\n"
               "1003|shared-category-2\n1004|category-1005\n"
               "1005|category-1004"
        ),
        "unique_indexes_in_both_databases": all(
            sql(database, "SELECT indexname FROM pg_indexes WHERE schemaname='public' "
                "AND indexname IN ('IX_Posts_Slug','IX_Categories_Slug') "
                "ORDER BY indexname")
            == "IX_Categories_Slug\nIX_Posts_Slug"
            for database in ("f20_empty", "f20_prior")
        ),
        "database_rejects_duplicate_slug": sql(
            "f20_prior",
            "INSERT INTO \"Categories\" (\"Name\", \"Slug\", \"CreatedDate\", \"IsActive\") "
            "VALUES ('F20 Rejected', 'shared-category', now(), true)",
            expect_failure=True,
        ) == "true",
    }

    visitor, _ = cookie_opener()
    wait_until_ready(visitor, base + "/", "DEVCORE", 30)
    old_post = request(visitor, base + "/post/shared-post")
    old_category = request(visitor, base + "/kategori/shared-category")
    checks["old_urls_keep_first_owner"] = (
        old_post[0] == old_category[0] == 200
        and "old owner" in old_post[2].lower()
        and "F20 Old Owner" in old_category[2]
    )

    admin, _ = cookie_opener()
    _, login_token, login_status, login_url, _ = submit_login(
        admin, base, os.environ["DEVCORE_TEST_ADMIN_USERNAME"],
        os.environ["DEVCORE_TEST_ADMIN_PASSWORD"],
    )
    checks["admin_session"] = (
        login_token is not None and login_status == 200
        and "/Admin/Dashboard" in login_url
    )

    category_url = base + "/AdminCategory/Create"
    category_get, _, category_body = request(admin, category_url)
    category_token = extract_antiforgery_token(category_body)

    def create_pair(url: str, form: dict[str, str]) -> list[tuple[int, str, str]]:
        barrier = threading.Barrier(2)

        def create_one() -> tuple[int, str, str]:
            barrier.wait(timeout=5)
            return request(admin, url, data=form)

        with ThreadPoolExecutor(max_workers=2) as pool:
            futures = [pool.submit(create_one) for _ in range(2)]
            return [future.result(timeout=15) for future in futures]

    category_pair = create_pair(category_url, {
        "Name": "F20 Race Category",
        "__RequestVerificationToken": category_token or "",
    })
    checks["concurrent_categories_get_distinct_slugs"] = (
        category_get == 200 and category_token is not None
        and all(status == 200 and url.endswith("/AdminCategory")
                for status, url, _ in category_pair)
        and sql("f20_prior", "SELECT \"Slug\" FROM \"Categories\" "
                "WHERE \"Name\"='F20 Race Category' ORDER BY \"Slug\"")
        == "f20-race-category\nf20-race-category-2"
    )

    category_symbol = request(admin, category_url, data={
        "Name": "!!!", "__RequestVerificationToken": category_token or "",
    })
    category_turkish = request(admin, category_url, data={
        "Name": "İçerik Çözümleri", "__RequestVerificationToken": category_token or "",
    })
    checks["category_empty_and_turkish_fallback"] = (
        category_symbol[0] == category_turkish[0] == 200
        and sql("f20_prior", "SELECT \"Slug\" FROM \"Categories\" "
                "WHERE \"Name\"='!!!'") == "category"
        and sql("f20_prior", "SELECT \"Slug\" FROM \"Categories\" "
                "WHERE \"Name\"='İçerik Çözümleri'") == "icerik-cozumleri"
    )

    post_url = base + "/AdminPost/Create"
    post_get, _, post_body = request(admin, post_url)
    post_token = extract_antiforgery_token(post_body)

    def post_form(title: str) -> dict[str, str]:
        return {
            "Title": title, "Content": "F20 concurrent post content",
            "CategoryId": "1001", "Summary": "F20_TEST_POST",
            "Excerpt": "F20 excerpt", "IsPublished": "true",
            "IsActive": "true", "PublishDate": "2026-01-01T12:00:00",
            "__RequestVerificationToken": post_token or "",
        }

    post_pair = create_pair(post_url, post_form("F20 Race Post"))
    checks["concurrent_posts_get_distinct_slugs"] = (
        post_get == 200 and post_token is not None
        and all(status == 200 and url.endswith("/AdminPost")
                for status, url, _ in post_pair)
        and sql("f20_prior", "SELECT \"Slug\" FROM \"Posts\" "
                "WHERE \"Title\"='F20 Race Post' ORDER BY \"Slug\"")
        == "f20-race-post\nf20-race-post-2"
    )
    post_symbol = request(admin, post_url, data=post_form("!!!"))
    post_turkish = request(admin, post_url, data=post_form("Çığ Örnek"))
    checks["post_empty_and_turkish_fallback"] = (
        post_symbol[0] == post_turkish[0] == 200
        and sql("f20_prior", "SELECT \"Slug\" FROM \"Posts\" "
                "WHERE \"Title\"='!!!'") == "post"
        and sql("f20_prior", "SELECT \"Slug\" FROM \"Posts\" "
                "WHERE \"Title\"='Çığ Örnek'") == "cig-ornek"
    )

    for name, passed in checks.items():
        print(f"f20_{name}={str(passed).lower()}")
    return 0 if all(checks.values()) else 1


if __name__ == "__main__":
    raise SystemExit(main())
