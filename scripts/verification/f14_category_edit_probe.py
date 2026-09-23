#!/usr/bin/env python3
"""Verify category edits preserve server-owned fields and reject missing rows."""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
from dataclasses import dataclass

from http_probe_support import (
    cookie_opener,
    extract_antiforgery_token,
    has_authentication_cookie,
    request,
    submit_login,
    wait_until_ready,
)


INACTIVE_CATEGORY_ID = 1002
MISSING_CATEGORY_ID = 999999


@dataclass(frozen=True)
class CategoryRow:
    name: str
    slug: str
    created_date: str
    is_active: bool


def read_category(args, category_id: int) -> CategoryRow | None:
    sql = (
        'SELECT "Name", "Slug", "CreatedDate", "IsActive" '
        f'FROM "Categories" WHERE "Id" = {category_id};'
    )
    process = subprocess.run(
        [
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
            "-F",
            "\t",
            "-c",
            sql,
        ],
        check=True,
        capture_output=True,
        text=True,
    )
    row = process.stdout.strip()
    if not row:
        return None

    fields = row.split("\t")
    if len(fields) != 4:
        raise RuntimeError("Unexpected category query result shape.")
    return CategoryRow(
        name=fields[0],
        slug=fields[1],
        created_date=fields[2],
        is_active=fields[3] == "t",
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
    username = os.environ.get("DEVCORE_TEST_ADMIN_USERNAME", "f14-admin")
    password = os.environ.get("DEVCORE_TEST_ADMIN_PASSWORD", "")
    if not password:
        print("DEVCORE_TEST_ADMIN_PASSWORD is required.", file=sys.stderr)
        return 2

    before = read_category(args, INACTIVE_CATEGORY_ID)
    if before is None:
        print("F14 inactive category fixture is missing.", file=sys.stderr)
        return 1

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
        print("F14 could not establish an authenticated fixture session.", file=sys.stderr)
        return 1

    edit_url = f"{base_url}/AdminCategory/Edit/{INACTIVE_CATEGORY_ID}"
    edit_status, _, edit_body = request(opener, edit_url)
    edit_token = extract_antiforgery_token(edit_body)
    updated_name = "F14 Renamed Inactive Category"
    update_status = 0
    update_url = ""
    update_body = ""
    if edit_token is not None:
        update_status, update_url, update_body = request(
            opener,
            edit_url,
            data={
                "Id": str(INACTIVE_CATEGORY_ID),
                "Name": updated_name,
                "Slug": "attacker-controlled-category-slug",
                "CreatedDate": "2001-01-01T00:00:00Z",
                "IsActive": "true",
                "Description": "field-that-does-not-exist",
                "__RequestVerificationToken": edit_token,
            },
        )

    after = read_category(args, INACTIVE_CATEGORY_ID)
    refreshed_status, _, refreshed_body = request(opener, edit_url)
    missing_get_status, _, _ = request(
        opener,
        f"{base_url}/AdminCategory/Edit/{MISSING_CATEGORY_ID}",
    )

    missing_token = extract_antiforgery_token(refreshed_body)
    missing_post_status = 0
    if missing_token is not None:
        missing_post_status, _, _ = request(
            opener,
            f"{base_url}/AdminCategory/Edit/{MISSING_CATEGORY_ID}",
            data={
                "Id": str(MISSING_CATEGORY_ID),
                "Name": "F14 Missing Category",
                "__RequestVerificationToken": missing_token,
            },
        )

    after_missing_post = read_category(args, INACTIVE_CATEGORY_ID)
    checks = {
        "edit_form_exposes_only_editable_category_fields": (
            edit_status == 200
            and edit_token is not None
            and 'name="Id"' in edit_body
            and 'name="Name"' in edit_body
            and all(
                f'name="{field}"' not in edit_body
                for field in ("Slug", "CreatedDate", "IsActive", "Description")
            )
        ),
        "name_update_succeeds": (
            update_status == 200
            and update_url.rstrip("/").endswith("/AdminCategory")
            and updated_name in update_body
            and refreshed_status == 200
            and updated_name in refreshed_body
        ),
        "created_date_and_inactive_state_are_preserved": (
            after is not None
            and after.created_date == before.created_date
            and not before.is_active
            and not after.is_active
        ),
        "unexpected_fields_cannot_overwrite_server_state": (
            after is not None
            and after.slug == "f14-renamed-inactive-category"
            and after.slug != "attacker-controlled-category-slug"
            and after.created_date == before.created_date
            and not after.is_active
        ),
        "missing_category_get_and_post_return_not_found": (
            missing_get_status == 404
            and missing_post_status == 404
        ),
        "missing_update_has_no_side_effect_on_existing_category": (
            after is not None
            and after_missing_post == after
        ),
    }
    statuses = {
        "edit_get": edit_status,
        "edit_save_final": update_status,
        "refreshed_edit_get": refreshed_status,
        "missing_get": missing_get_status,
        "missing_post": missing_post_status,
    }

    print("F14 category edit preservation verification")
    print(json.dumps({"checks": checks, "statuses": statuses}, indent=2))
    failed = [name for name, passed in checks.items() if not passed]
    if failed:
        print(f"F14 verification failed: {', '.join(failed)}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
