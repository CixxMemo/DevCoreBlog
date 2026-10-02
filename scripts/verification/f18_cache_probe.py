#!/usr/bin/env python3
"""Exercise F18 output cache against a disposable PostgreSQL and HTTP host."""

import argparse
from concurrent.futures import ThreadPoolExecutor
import os
import subprocess
import time

from http_probe_support import (
    cookie_opener,
    extract_antiforgery_token,
    request,
    request_with_headers,
    submit_login,
)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-url", required=True)
    parser.add_argument("--pg-port", required=True)
    parser.add_argument("--pg-user", required=True)
    args = parser.parse_args()
    base = args.base_url.rstrip("/")

    def sql(statement: str) -> None:
        subprocess.run(
            ["psql", "-h", "127.0.0.1", "-p", args.pg_port,
             "-U", args.pg_user, "-d", "devcoreblog_f01_test",
             "-v", "ON_ERROR_STOP=1", "-c", statement],
            check=True, stdout=subprocess.DEVNULL,
        )

    visitor, _ = cookie_opener()
    warm_home = request_with_headers(visitor, base + "/")
    warm_category = request_with_headers(visitor, base + "/kategori/f01-active")
    # Every card displays its title; compact featured cards intentionally omit summaries.
    sql('UPDATE "Posts" SET "Title" = \'F18_DB_ONLY_CHANGED\' WHERE "Id" = 2001')
    cached_home = request_with_headers(visitor, base + "/")
    cached_category = request_with_headers(visitor, base + "/kategori/f01-active")
    uncached_variant = request(visitor, base + "/?unrecognized=1")

    admin, _ = cookie_opener()
    _, token, login_status, login_url, _ = submit_login(
        admin, base, os.environ["DEVCORE_TEST_ADMIN_USERNAME"],
        os.environ["DEVCORE_TEST_ADMIN_PASSWORD"],
    )
    edit_status, _, edit_body = request(admin, base + "/AdminPost/Edit/2001")
    toggle_token = extract_antiforgery_token(edit_body)
    toggle_status, _, toggle_body = request(
        admin, base + "/AdminPost/TogglePublish/2001",
        data={"__RequestVerificationToken": toggle_token or ""},
    )

    def parallel_home(_: int) -> tuple[int, str]:
        local_visitor, _ = cookie_opener()
        status, _, body = request(local_visitor, base + "/")
        return status, body

    with ThreadPoolExecutor(max_workers=8) as pool:
        parallel_responses = list(pool.map(parallel_home, range(24)))
    after_unpublish = request(visitor, base + "/")
    after_unpublish_category = request(visitor, base + "/kategori/f01-active")

    sql('UPDATE "Posts" SET "PublishDate" = CURRENT_TIMESTAMP + interval \'3 seconds\' '
        'WHERE "Id" = 2002')
    before_schedule = request(visitor, base + "/")
    deadline = time.monotonic() + 8
    scheduled_body = ""
    while time.monotonic() < deadline:
        _, _, scheduled_body = request(visitor, base + "/")
        if "F01_FUTURE_VISIBLE_MARKER" in scheduled_body:
            break
        time.sleep(0.25)

    create_status, _, create_body = request(admin, base + "/AdminCategory/Create")
    create_token = extract_antiforgery_token(create_body)
    category_save_status, category_save_url, _ = request(
        admin, base + "/AdminCategory/Create",
        data={"Name": "F18 New Category",
              "__RequestVerificationToken": create_token or ""},
    )

    def create_parallel_category(index: int) -> int:
        status, _, _ = request(
            admin, base + "/AdminCategory/Create",
            data={"Name": f"F18 Parallel Category {index}",
                  "__RequestVerificationToken": create_token or ""},
        )
        return status

    with ThreadPoolExecutor(max_workers=4) as pool:
        parallel_saves = list(pool.map(create_parallel_category, range(4)))
    print(f"f18_parallel_mutation_statuses={parallel_saves}")
    category_after = request(visitor, base + "/")
    feed_headers = request_with_headers(visitor, base + "/api/public/posts/latest")[3]

    checks = {
        "cached_home_and_category_reused": (
            warm_home[0] == cached_home[0] == 200
            and warm_category[0] == cached_category[0] == 200
            and "F18_DB_ONLY_CHANGED" not in cached_home[2]
            and "F18_DB_ONLY_CHANGED" not in cached_category[2]
            and "F18_DB_ONLY_CHANGED" in uncached_variant[2]
        ),
        "unpublish_invalidates_warmed_lists": (
            login_status == edit_status == toggle_status == 200
            and token is not None and toggle_token is not None
            and "/Admin/Dashboard" in login_url
            and '"success":true' in toggle_body.lower()
            and "F01_VISIBLE_MARKER" not in after_unpublish[2]
            and "F18_DB_ONLY_CHANGED" not in after_unpublish[2]
            and "F01_VISIBLE_MARKER" not in after_unpublish_category[2]
        ),
        "parallel_reads_do_not_reinsert_stale_response": all(
            status == 200 and "F01_VISIBLE_MARKER" not in body
            and "F18_DB_ONLY_CHANGED" not in body
            for status, body in parallel_responses
        ),
        "scheduled_post_appears_at_boundary": (
            "F01_FUTURE_VISIBLE_MARKER" not in before_schedule[2]
            and "F01_FUTURE_VISIBLE_MARKER" in scheduled_body
        ),
        "category_mutation_invalidates_navigation": (
            create_status == category_save_status == 200
            and create_token is not None
            and category_save_url.rstrip("/").endswith("/AdminCategory")
            and "F18 New Category" in category_after[2]
        ),
        "parallel_mutations_do_not_reinsert_stale_navigation": (
            all(status == 200 for status in parallel_saves)
            and all(f"F18 Parallel Category {index}" in category_after[2]
                    for index in range(4))
        ),
        "public_client_responses_are_not_stored": all(
            "no-store" in response[3].get("Cache-Control", "").lower()
            for response in [cached_home, cached_category]
        ) and "no-store" in feed_headers.get("Cache-Control", "").lower(),
    }
    for name, passed in checks.items():
        print(f"f18_{name}={str(passed).lower()}")
    return 0 if all(checks.values()) else 1


if __name__ == "__main__":
    raise SystemExit(main())
