#!/usr/bin/env python3
"""Exercise admin publication dates against an isolated PostgreSQL database."""

from __future__ import annotations

import argparse
import html
import os
import re
import subprocess
import sys

from http_probe_support import (
    cookie_opener,
    extract_antiforgery_token,
    extract_hidden_value,
    has_authentication_cookie,
    request,
    submit_login,
    wait_until_ready,
)


def stored_row(args, title: str) -> tuple[str, str, str] | None:
    sql = (
        'SELECT "Id", to_char("PublishDate" AT TIME ZONE \'UTC\', '
        "'YYYY-MM-DD\"T\"HH24:MI:SS'), "
        'to_char("CreatedDate" AT TIME ZONE \'UTC\', '
        "'YYYY-MM-DD\"T\"HH24:MI:SS') "
        'FROM "Posts" WHERE "Title" = :\'probe_title\';'
    )
    result = subprocess.run(
        [
            "psql", "-X", "-h", "127.0.0.1", "-p", str(args.database_port),
            "-U", args.database_user, "-d", args.database_name,
            "-v", "ON_ERROR_STOP=1", "-v", f"probe_title={title}",
            "-A", "-t", "-F", "\t",
        ],
        input=sql, check=True, capture_output=True, text=True,
    )
    rows = result.stdout.strip().splitlines()
    if len(rows) != 1:
        return None
    columns = rows[0].split("\t")
    return tuple(columns) if len(columns) == 3 else None


def publish_value(page: str) -> str | None:
    for tag in re.findall(r"<input\b[^>]*>", page, re.IGNORECASE):
        if re.search(r'\bname="PublishDate"', tag):
            match = re.search(r'\bvalue="([^"]*)"', tag)
            return html.unescape(match.group(1)) if match else None
    return None


def existing_utc_value(args) -> str:
    result = subprocess.run(
        [
            "psql", "-X", "-h", "127.0.0.1", "-p", str(args.database_port),
            "-U", args.database_user, "-d", args.database_name,
            "-v", "ON_ERROR_STOP=1", "-A", "-t", "-c",
            'SELECT to_char("PublishDate" AT TIME ZONE \'UTC\', '
            "'YYYY-MM-DD\"T\"HH24:MI:SS.US') "
            'FROM "Posts" WHERE "Id" = 2002;',
        ],
        check=True, capture_output=True, text=True,
    )
    return result.stdout.strip()


def fields(title: str, date: str, token: str, post_id: str | None = None) -> dict[str, str]:
    result = {
        "Title": title,
        "Content": "F16 publication time verification content",
        "CategoryId": "1001",
        "Summary": "F16 publication time verification summary",
        "Excerpt": "F16 publication time verification excerpt",
        "IsPublished": "false",
        "IsActive": "true",
        "PublishDate": date,
        "__RequestVerificationToken": token,
    }
    if post_id is not None:
        result["Id"] = post_id
    return result


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-url", required=True)
    parser.add_argument("--database-port", type=int, required=True)
    parser.add_argument("--database-user", required=True)
    parser.add_argument("--database-name", required=True)
    parser.add_argument("--mode", choices=("roundtrip", "dst"), required=True)
    parser.add_argument("--marker", default="F16_TIME_ROUNDTRIP_UTC")
    args = parser.parse_args()

    password = os.environ.get("DEVCORE_TEST_ADMIN_PASSWORD", "")
    if not password:
        print("DEVCORE_TEST_ADMIN_PASSWORD is required.", file=sys.stderr)
        return 2

    base_url = args.base_url.rstrip("/")
    opener, cookies = cookie_opener()
    wait_until_ready(opener, f"{base_url}/Account/Login", "Admin Sign In", 30)
    _, token, status, url, _ = submit_login(
        opener, base_url, os.environ.get("DEVCORE_TEST_ADMIN_USERNAME", ""), password,
    )
    if token is None or status != 200 or "/Admin/Dashboard" not in url or not has_authentication_cookie(cookies):
        print("F16 could not establish an admin session.", file=sys.stderr)
        return 1

    create_status, _, create_page = request(opener, f"{base_url}/AdminPost/Create")
    create_token = extract_antiforgery_token(create_page)
    if create_status != 200 or create_token is None:
        print("F16 could not load the create form.", file=sys.stderr)
        return 1

    if args.mode == "dst":
        checks = {}
        for name, local_time in (
            ("invalid", "2026-03-08T02:30"),
            ("ambiguous", "2026-11-01T01:30"),
        ):
            title = f"F16_TIME_{name.upper()}"
            status, url, page = request(
                opener, f"{base_url}/AdminPost/Create",
                data=fields(title, local_time, create_token),
            )
            checks[name] = (
                status == 200
                and url.endswith("/AdminPost/Create")
                and title in page
                and (
                    "does not exist in the site time zone" in page
                    if name == "invalid"
                    else "occurs twice in the site time zone" in page
                )
                and stored_row(args, title) is None
            )
        for name, passed in checks.items():
            print(f"f16_{name}_time_rejected={str(passed).lower()}")
        return 0 if all(checks.values()) else 1

    local_time = "2026-09-27T12:34:56"
    expected_utc = "2026-09-27T09:34:56"
    status, url, _ = request(
        opener, f"{base_url}/AdminPost/Create",
        data=fields(args.marker, local_time, create_token),
    )
    row = stored_row(args, args.marker)
    created = status == 200 and url.rstrip("/").endswith("/AdminPost") and row is not None
    checks = {"create_succeeded": created}
    if not created:
        for name, passed in checks.items():
            print(f"f16_{name}={str(passed).lower()}")
        return 1

    post_id, stored_utc, created_utc = row
    edit_url = f"{base_url}/AdminPost/Edit/{post_id}"
    edit_status, _, edit_page = request(opener, edit_url)
    edit_token = extract_antiforgery_token(edit_page)
    checks["stored_utc_matches_site_time"] = stored_utc == expected_utc
    checks["edit_displays_site_time"] = (
        edit_status == 200 and (publish_value(edit_page) or "").startswith(local_time)
    )
    checks["form_identifies_site_time_zone"] = "Europe/Istanbul" in create_page and "Europe/Istanbul" in edit_page
    if edit_token is not None:
        edit_fields = fields(args.marker, local_time, edit_token, post_id)
        edit_fields["EditVersion"] = extract_hidden_value(edit_page, "EditVersion") or ""
        update_status, update_url, _ = request(
            opener, edit_url,
            data=edit_fields,
        )
        updated_row = stored_row(args, args.marker)
        checks["edit_roundtrip_preserves_utc"] = (
            update_status == 200
            and update_url.rstrip("/").endswith("/AdminPost")
            and updated_row is not None
            and updated_row[1] == expected_utc
            and updated_row[2] == created_utc
        )
    else:
        checks["edit_roundtrip_preserves_utc"] = False

    old_utc = existing_utc_value(args)
    existing_url = f"{base_url}/AdminPost/Edit/2002"
    old_status, _, old_page = request(opener, existing_url)
    old_token = extract_antiforgery_token(old_page)
    old_value = publish_value(old_page)
    if old_status == 200 and old_token and old_value:
        old_fields = fields("F01 Future Post", old_value, old_token, "2002")
        old_fields["EditVersion"] = extract_hidden_value(old_page, "EditVersion") or ""
        old_fields.update({
            "Content": "F01_FUTURE_VISIBLE_MARKER",
            "Summary": "F01_FUTURE_VISIBLE_MARKER",
            "Excerpt": "F01 future excerpt",
            "IsPublished": "true",
        })
        old_save_status, old_save_url, _ = request(
            opener, existing_url, data=old_fields,
        )
        checks["existing_fractional_utc_survives_edit"] = (
            old_save_status == 200
            and old_save_url.rstrip("/").endswith("/AdminPost")
            and existing_utc_value(args) == old_utc
        )
    else:
        checks["existing_fractional_utc_survives_edit"] = False

    for name, passed in checks.items():
        print(f"f16_{name}={str(passed).lower()}")
    return 0 if all(checks.values()) else 1


if __name__ == "__main__":
    raise SystemExit(main())
