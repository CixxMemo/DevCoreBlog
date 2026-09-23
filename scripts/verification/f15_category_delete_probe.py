#!/usr/bin/env python3
"""Verify safe category deletion, including the insert/delete race."""

from __future__ import annotations

import argparse
import json
import os
import select
import subprocess
import sys
import time

from http_probe_support import (
    cookie_opener,
    extract_antiforgery_token,
    has_authentication_cookie,
    request,
    submit_login,
    wait_until_ready,
)


EMPTY_CATEGORY_ID = 9015
RACE_CATEGORY_ID = 9016
RACE_POST_ID = 9916
PROTECTED_CATEGORY_ID = 1001
BLOCKED_MESSAGE = (
    "This category contains posts. Move or delete them before deleting the category."
)


def psql_command(args) -> list[str]:
    return [
        "psql",
        "-X",
        "-h",
        args.database_host,
        "-p",
        str(args.database_port),
        "-U",
        args.database_user,
        "-d",
        args.database_name,
        "-v",
        "ON_ERROR_STOP=1",
        "-A",
        "-t",
    ]


def execute_sql(args, sql: str) -> str:
    completed = subprocess.run(
        [*psql_command(args), "-c", sql],
        check=True,
        capture_output=True,
        text=True,
    )
    return completed.stdout.strip()


def delete_category(opener, base_url: str, category_id: int):
    index_status, _, index_body = request(opener, f"{base_url}/AdminCategory")
    token = extract_antiforgery_token(index_body)
    if index_status != 200 or token is None:
        raise RuntimeError("Could not obtain the category deletion antiforgery token.")
    return request(
        opener,
        f"{base_url}/AdminCategory/Delete/{category_id}",
        data={"__RequestVerificationToken": token},
    )


def start_concurrent_post_insert(args) -> subprocess.Popen[str]:
    sql = f"""
        BEGIN;
        SET LOCAL statement_timeout = '10s';
        SELECT "Id"
        FROM "Categories"
        WHERE "Id" = {RACE_CATEGORY_ID}
        FOR KEY SHARE;
        SELECT pg_sleep(1.5);
        INSERT INTO "Posts" (
            "Id", "Title", "Slug", "Summary", "Content", "CategoryId",
            "CreatedDate", "IsActive", "ViewCount", "PublishDate", "Excerpt",
            "IsPublished", "ThumbnailUrl"
        ) VALUES (
            {RACE_POST_ID}, 'F15 Concurrent Post', 'f15-concurrent-post',
            'F15 concurrent summary', 'F15 concurrent content', {RACE_CATEGORY_ID},
            CURRENT_TIMESTAMP, true, 0, CURRENT_TIMESTAMP,
            'F15 concurrent excerpt', false, ''
        );
        COMMIT;
    """
    process = subprocess.Popen(
        [*psql_command(args), "-f", "-"],
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        bufsize=1,
    )
    if process.stdin is None:
        raise RuntimeError("Could not open the concurrent PostgreSQL session.")
    process.stdin.write(sql)
    process.stdin.close()
    process.stdin = None
    return process


def wait_for_category_lock(process: subprocess.Popen[str]) -> None:
    if process.stdout is None:
        raise RuntimeError("Concurrent PostgreSQL output is unavailable.")

    deadline = time.monotonic() + 5
    while time.monotonic() < deadline:
        ready, _, _ = select.select([process.stdout], [], [], 0.25)
        if not ready:
            if process.poll() is not None:
                break
            continue
        line = process.stdout.readline().strip()
        if line == str(RACE_CATEGORY_ID):
            return

    stderr = process.stderr.read() if process.stderr is not None else ""
    raise RuntimeError(
        "The concurrent insert did not acquire its category lock. "
        f"PostgreSQL output: {stderr.strip()}"
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-url", default="http://127.0.0.1:15159")
    parser.add_argument("--wait-seconds", type=int, default=30)
    parser.add_argument("--database-host", default="127.0.0.1")
    parser.add_argument("--database-port", type=int, required=True)
    parser.add_argument("--database-user", required=True)
    parser.add_argument("--database-name", required=True)
    args = parser.parse_args()

    base_url = args.base_url.rstrip("/")
    username = os.environ.get("DEVCORE_TEST_ADMIN_USERNAME", "f15-admin")
    password = os.environ.get("DEVCORE_TEST_ADMIN_PASSWORD", "")
    if not password:
        print("DEVCORE_TEST_ADMIN_PASSWORD is required.", file=sys.stderr)
        return 2

    execute_sql(
        args,
        f"""
            INSERT INTO "Categories" (
                "Id", "Name", "Slug", "CreatedDate", "IsActive"
            ) VALUES
                ({EMPTY_CATEGORY_ID}, 'F15 Empty Category',
                 'f15-empty-category', CURRENT_TIMESTAMP, true),
                ({RACE_CATEGORY_ID}, 'F15 Race Category',
                 'f15-race-category', CURRENT_TIMESTAMP, true);
        """,
    )

    opener, cookies = cookie_opener()
    wait_until_ready(
        opener,
        f"{base_url}/Account/Login",
        "Admin Sign In",
        args.wait_seconds,
    )
    _, login_token, login_status, login_url, _ = submit_login(
        opener,
        base_url,
        username,
        password,
    )
    if (
        login_token is None
        or login_status != 200
        or "/Admin/Dashboard" not in login_url
        or not has_authentication_cookie(cookies)
    ):
        print("F15 could not establish an authenticated fixture session.", file=sys.stderr)
        return 1

    empty_status, empty_url, empty_body = delete_category(
        opener,
        base_url,
        EMPTY_CATEGORY_ID,
    )
    empty_count = int(
        execute_sql(
            args,
            f'SELECT COUNT(*) FROM "Categories" WHERE "Id" = {EMPTY_CATEGORY_ID};',
        )
    )

    protected_posts_before = int(
        execute_sql(
            args,
            f'SELECT COUNT(*) FROM "Posts" WHERE "CategoryId" = {PROTECTED_CATEGORY_ID};',
        )
    )
    protected_status, protected_url, protected_body = delete_category(
        opener,
        base_url,
        PROTECTED_CATEGORY_ID,
    )
    protected_state = execute_sql(
        args,
        f"""
            SELECT
                (SELECT COUNT(*) FROM "Categories"
                 WHERE "Id" = {PROTECTED_CATEGORY_ID}) || '|' ||
                (SELECT COUNT(*) FROM "Posts"
                 WHERE "CategoryId" = {PROTECTED_CATEGORY_ID});
        """,
    )

    concurrent_process = start_concurrent_post_insert(args)
    try:
        wait_for_category_lock(concurrent_process)
        race_status, race_url, race_body = delete_category(
            opener,
            base_url,
            RACE_CATEGORY_ID,
        )
        concurrent_exit = concurrent_process.wait(timeout=8)
        concurrent_error = (
            concurrent_process.stderr.read().strip()
            if concurrent_process.stderr is not None
            else ""
        )
    finally:
        if concurrent_process.poll() is None:
            concurrent_process.kill()
            concurrent_process.wait(timeout=2)

    race_state = execute_sql(
        args,
        f"""
            SELECT
                (SELECT COUNT(*) FROM "Categories"
                 WHERE "Id" = {RACE_CATEGORY_ID}) || '|' ||
                (SELECT COUNT(*) FROM "Posts"
                 WHERE "Id" = {RACE_POST_ID}
                   AND "CategoryId" = {RACE_CATEGORY_ID});
        """,
    )

    checks = {
        "empty_category_is_deleted": (
            empty_status == 200
            and empty_url.rstrip("/").endswith("/AdminCategory")
            and BLOCKED_MESSAGE not in empty_body
            and empty_count == 0
        ),
        "linked_category_is_blocked_without_data_loss": (
            protected_status == 200
            and protected_url.rstrip("/").endswith("/AdminCategory")
            and BLOCKED_MESSAGE in protected_body
            and protected_state == f"1|{protected_posts_before}"
            and protected_posts_before > 0
        ),
        "concurrent_insert_commits": (
            concurrent_exit == 0 and not concurrent_error
        ),
        "concurrent_delete_is_blocked_without_data_loss": (
            race_status == 200
            and race_url.rstrip("/").endswith("/AdminCategory")
            and BLOCKED_MESSAGE in race_body
            and race_state == "1|1"
        ),
    }
    report = {
        "checks": checks,
        "statuses": {
            "empty_delete_final": empty_status,
            "linked_delete_final": protected_status,
            "concurrent_delete_final": race_status,
        },
        "database_state": {
            "empty_category_count": empty_count,
            "protected_category_and_posts": protected_state,
            "concurrent_category_and_post": race_state,
        },
    }
    print("F15 category deletion verification")
    print(json.dumps(report, indent=2))
    failed = [name for name, passed in checks.items() if not passed]
    if failed:
        print(f"F15 verification failed: {', '.join(failed)}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
