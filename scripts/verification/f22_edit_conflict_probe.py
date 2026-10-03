#!/usr/bin/env python3
"""Exercise stale post/category edit forms against an isolated PostgreSQL database."""

import argparse
from concurrent.futures import ThreadPoolExecutor
import html
import os
from pathlib import Path
import re
import subprocess
import threading

from http_probe_support import cookie_opener, extract_antiforgery_token, request, submit_login, wait_until_ready


def field(body: str, name: str) -> str:
    match = re.search(r'<input[^>]*\bname="' + re.escape(name) + r'"[^>]*>', body)
    if not match:
        return ""
    value = re.search(r'\bvalue="([^"]*)"', match.group())
    return html.unescape(value.group(1)) if value else ""


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-url", required=True)
    parser.add_argument("--pg-port", required=True)
    parser.add_argument("--pg-user", required=True)
    args = parser.parse_args()

    def sql(statement: str, database: str = "f22_prior") -> str:
        result = subprocess.run(
            ["psql", "-X", "-h", "127.0.0.1", "-p", args.pg_port,
             "-U", args.pg_user, "-d", database, "-v", "ON_ERROR_STOP=1",
             "-A", "-t", "-c", statement], capture_output=True, text=True, check=True)
        return result.stdout.strip()

    base = args.base_url.rstrip("/")
    admin, _ = cookie_opener()
    wait_until_ready(admin, base + "/", "DEVCORE", 30)
    _, token, status, url, _ = submit_login(
        admin, base, os.environ["DEVCORE_TEST_ADMIN_USERNAME"],
        os.environ["DEVCORE_TEST_ADMIN_PASSWORD"])
    checks = {"admin_session": token is not None and status == 200 and "/Admin/Dashboard" in url}
    expected_migrations = sorted(p.stem for p in (Path(__file__).resolve().parents[2] / "Migrations").glob("*.cs")
        if re.fullmatch(r"[0-9]{14}_.+", p.stem) and not p.name.endswith(".Designer.cs"))
    checks["empty_and_prior_migrations"] = bool(expected_migrations) and all(
        sql('SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId"', database).splitlines() == expected_migrations
        for database in ("f22_empty", "f22_prior"))
    checks["existing_rows_start_at_version_one"] = (
        sql('SELECT "EditVersion" FROM "Posts" WHERE "Id"=2001') == "1" and
        sql('SELECT "EditVersion" FROM "Categories" WHERE "Id"=1001') == "1")

    category_url = base + "/AdminCategory/Edit/1001"
    first_category = request(admin, category_url)
    second_category = request(admin, category_url)
    category_token = extract_antiforgery_token(first_category[2]) or ""
    category_version = field(first_category[2], "EditVersion")
    common_category = {"Id": "1001", "EditVersion": category_version,
                       "__RequestVerificationToken": category_token}
    category_save = request(admin, category_url, data={**common_category, "Name": "F22 first category"})
    category_conflict = request(admin, category_url, data={**common_category, "Name": "F22 second category"})
    checks["category_conflict_preserves_first_and_input"] = (
        category_save[1].endswith("/AdminCategory") and
        category_conflict[0] == 409 and
        "changed since" in category_conflict[2].lower() and
        'value="F22 second category"' in category_conflict[2] and
        "Reload current category" in category_conflict[2] and
        sql('SELECT "Name" FROM "Categories" WHERE "Id"=1001') == "F22 first category")

    post_url = base + "/AdminPost/Edit/2001"
    first_post = request(admin, post_url)
    second_post = request(admin, post_url)
    post_token = extract_antiforgery_token(first_post[2]) or ""
    post_version = field(first_post[2], "EditVersion")
    post_base = {"Id": "2001", "Content": "F22 changed body", "CategoryId": "1001",
                 "Summary": "old owner", "Excerpt": "", "IsPublished": "true",
                 "IsActive": "true", "SaveAction": "Save", "PublishDate": field(first_post[2], "PublishDate"),
                 "EditVersion": post_version, "__RequestVerificationToken": post_token}
    post_save = request(admin, post_url, data={**post_base, "Title": "F22 first post"})
    post_conflict = request(admin, post_url, data={**post_base, "Title": "F22 second post"})
    checks["post_conflict_preserves_first_and_input"] = (
        post_save[1].endswith("/AdminPost") and
        post_conflict[0] == 409 and
        "changed since" in post_conflict[2].lower() and
        'value="F22 second post"' in post_conflict[2] and
        "Reload current post" in post_conflict[2] and
        sql('SELECT "Title" FROM "Posts" WHERE "Id"=2001') == "F22 first post")

    # A visitor counter increment must not invalidate an otherwise fresh edit.
    fresh = request(admin, post_url)
    fresh_token = extract_antiforgery_token(fresh[2]) or ""
    count_before = sql('SELECT "ViewCount" FROM "Posts" WHERE "Id"=2001')
    public_get = request(cookie_opener()[0], base + "/post/shared-post")
    counter_form = {**post_base, "Title": "F22 after counter",
                    "EditVersion": field(fresh[2], "EditVersion"),
                    "__RequestVerificationToken": fresh_token}
    counter_save = request(admin, post_url, data=counter_form)
    checks["counter_does_not_conflict"] = (
        public_get[0] == 200 and counter_save[1].endswith("/AdminPost") and
        sql('SELECT "Title" FROM "Posts" WHERE "Id"=2001') == "F22 after counter" and
        int(sql('SELECT "ViewCount" FROM "Posts" WHERE "Id"=2001')) > int(count_before))
    checks["only_successful_edits_advance_versions"] = (
        sql('SELECT "EditVersion" FROM "Categories" WHERE "Id"=1001') == "2" and
        sql('SELECT "EditVersion" FROM "Posts" WHERE "Id"=2001') == "3")

    concurrent_form = request(admin, category_url)
    concurrent_version = field(concurrent_form[2], "EditVersion")
    concurrent_token = extract_antiforgery_token(concurrent_form[2]) or ""
    barrier = threading.Barrier(2)

    def simultaneous_edit(name: str) -> tuple[int, str, str]:
        barrier.wait(timeout=5)
        return request(admin, category_url, data={
            "Id": "1001", "EditVersion": concurrent_version, "Name": name,
            "__RequestVerificationToken": concurrent_token})

    with ThreadPoolExecutor(max_workers=2) as pool:
        outcomes = list(pool.map(simultaneous_edit,
                                 ("F22 parallel A", "F22 parallel B")))
    winner = sql('SELECT "Name" FROM "Categories" WHERE "Id"=1001')
    checks["simultaneous_updates_have_one_winner"] = (
        sum(url.endswith("/AdminCategory") for _, url, _ in outcomes) == 1 and
        sum(status == 409 for status, _, _ in outcomes) == 1 and
        winner in ("F22 parallel A", "F22 parallel B") and
        sql('SELECT "EditVersion" FROM "Categories" WHERE "Id"=1001') == "3")

    for name, passed in checks.items():
        print(f"f22_{name}={str(passed).lower()}")
    return 0 if all(checks.values()) else 1


if __name__ == "__main__":
    raise SystemExit(main())
