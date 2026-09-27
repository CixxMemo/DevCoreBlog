#!/usr/bin/env python3
"""Verify that only eligible public GETs atomically change ViewCount."""

import argparse
from concurrent.futures import ThreadPoolExecutor
import os
import subprocess
import urllib.error
import urllib.request

from http_probe_support import cookie_opener, request, submit_login


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-url", required=True)
    parser.add_argument("--pg-port", required=True)
    parser.add_argument("--pg-user", required=True)
    args = parser.parse_args()
    base = args.base_url.rstrip("/")

    def sql(statement: str) -> str:
        completed = subprocess.run(
            ["psql", "-X", "-h", "127.0.0.1", "-p", args.pg_port,
             "-U", args.pg_user, "-d", "devcoreblog_f01_test",
             "-v", "ON_ERROR_STOP=1", "-A", "-t", "-c", statement],
            check=True, capture_output=True, text=True,
        )
        return completed.stdout.strip()

    sql("""
        CREATE TABLE f21_non_counter_writes (post_id integer NOT NULL);
        CREATE FUNCTION f21_detect_non_counter_update() RETURNS trigger
        LANGUAGE plpgsql AS $$
        BEGIN
            INSERT INTO f21_non_counter_writes (post_id) VALUES (NEW."Id");
            RETURN NEW;
        END
        $$;
        CREATE TRIGGER f21_non_counter_update
        BEFORE UPDATE OF "Title", "Slug", "Summary", "Content", "CreatedDate",
            "IsActive", "IsPublished", "CategoryId", "PublishDate", "Excerpt",
            "ThumbnailUrl" ON "Posts"
        FOR EACH ROW EXECUTE FUNCTION f21_detect_non_counter_update();
    """)

    def snapshot() -> str:
        return sql('SELECT "Title", "Slug", "Summary", "Content", '
                   '"IsActive", "IsPublished", "CategoryId", "PublishDate", '
                   '"Excerpt", "ThumbnailUrl", "CreatedDate" '
                   'FROM "Posts" WHERE "Id" = 2001')

    def count() -> int:
        return int(sql('SELECT "ViewCount" FROM "Posts" WHERE "Id" = 2001'))

    visitor, _ = cookie_opener()
    post_url = base + "/post/f01-visible"
    initial_snapshot = snapshot()
    initial_count = count()

    head_request = urllib.request.Request(post_url, method="HEAD")
    try:
        with visitor.open(head_request, timeout=5) as response:
            head_status = response.status
    except urllib.error.HTTPError as error:
        head_status = error.code
    after_head = count()

    admin, _ = cookie_opener()
    _, login_token, login_status, login_url, _ = submit_login(
        admin, base, os.environ["DEVCORE_TEST_ADMIN_USERNAME"],
        os.environ["DEVCORE_TEST_ADMIN_PASSWORD"],
    )
    admin_status, _, _ = request(admin, post_url)
    after_admin = count()

    request_count = 24
    def public_get(_: int) -> int:
        opener, _ = cookie_opener()
        status, _, _ = request(opener, post_url)
        return status

    with ThreadPoolExecutor(max_workers=12) as pool:
        public_statuses = list(pool.map(public_get, range(request_count)))
    final_count = count()

    # A row deleted before a public request must remain a 404 without a write.
    sql("""
        INSERT INTO "Posts" (
            "Id", "Title", "Slug", "Summary", "Content", "CreatedDate",
            "IsActive", "IsPublished", "CategoryId", "ViewCount",
            "PublishDate", "Excerpt", "ThumbnailUrl"
        ) VALUES (
            2999, 'F21 Removed', 'f21-removed', 'removed', 'removed',
            '2026-01-01T00:00:00Z', true, true, 1001, 0,
            '2026-01-01T00:00:00Z', '', ''
        );
        DELETE FROM "Posts" WHERE "Id" = 2999;
    """)
    removed_status, _, _ = request(visitor, base + "/post/f21-removed")

    hidden_slugs = (
        "f01-draft", "f01-future-visible-marker", "f01-inactive-post",
        "f01-duplicate-title",
    )
    hidden_statuses = [
        request(visitor, base + "/post/" + slug)[0] for slug in hidden_slugs
    ]
    hidden_counts = sql('SELECT "Id", "ViewCount" FROM "Posts" '
                        'WHERE "Id" IN (2002, 2003, 2004, 2008) ORDER BY "Id"')

    checks = {
        "head_does_not_count": head_status == 200 and after_head == initial_count,
        "authenticated_admin_does_not_count": (
            login_token is not None and login_status == 200
            and "/Admin/Dashboard" in login_url and admin_status == 200
            and after_admin == after_head
        ),
        "parallel_public_gets_are_atomic": (
            public_statuses == [200] * request_count
            and final_count - after_admin == request_count
        ),
        "only_counter_column_is_updated": (
            sql("SELECT count(*) FROM f21_non_counter_writes") == "0"
            and snapshot() == initial_snapshot
        ),
        "deleted_post_returns_404": removed_status == 404,
        "hidden_posts_do_not_count": (
            hidden_statuses == [404] * len(hidden_slugs)
            and hidden_counts == "2002|0\n2003|0\n2004|0\n2008|0"
        ),
    }
    for name, passed in checks.items():
        print(f"f21_{name}={str(passed).lower()}")
    print(f"f21_public_request_count={request_count}")
    print(f"f21_final_counter_delta={final_count - after_admin}")
    print("f21_counter_definition=eligible_public_get_requests")
    return 0 if all(checks.values()) else 1


if __name__ == "__main__":
    raise SystemExit(main())
