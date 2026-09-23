#!/usr/bin/env python3
"""Verify shared post/category rules through MVC and the preserved webhook."""

from __future__ import annotations

import argparse
import json
import os
import sys

from http_probe_support import (
    cookie_opener,
    extract_antiforgery_token,
    has_authentication_cookie,
    request,
    submit_login,
    wait_until_ready,
)


def post_fields(title: str, summary: str) -> dict[str, str]:
    return {
        "Title": title,
        "Content": "F12 shared validation content",
        "CategoryId": "1001",
        "Summary": summary,
        "Excerpt": "F12 excerpt",
        "IsPublished": "false",
        "IsActive": "true",
        "PublishDate": "2026-09-23T12:00",
    }


def webhook_request(opener, base_url: str, secret: str, payload: dict):
    status, _, body = request(
        opener,
        f"{base_url}/api/webhooks/posts",
        raw_data=json.dumps(payload).encode(),
        headers={
            "Content-Type": "application/json",
            "X-DevCore-Secret": secret,
        },
    )
    try:
        parsed = json.loads(body)
    except json.JSONDecodeError:
        parsed = {}
    return status, parsed


def error_fields(payload: dict) -> set[str]:
    errors = payload.get("errors", [])
    return {
        item.get("field")
        for item in errors
        if isinstance(item, dict) and isinstance(item.get("field"), str)
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-url", default="http://127.0.0.1:15159")
    parser.add_argument("--wait-seconds", type=int, default=30)
    args = parser.parse_args()

    base_url = args.base_url.rstrip("/")
    username = os.environ.get("DEVCORE_TEST_ADMIN_USERNAME", "f12-admin")
    password = os.environ.get("DEVCORE_TEST_ADMIN_PASSWORD", "")
    webhook_secret = os.environ.get("DEVCORE_TEST_WEBHOOK_SECRET", "")
    if not password or not webhook_secret:
        print("F12 test credentials are required.", file=sys.stderr)
        return 2

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
        print("F12 could not establish an authenticated fixture session.", file=sys.stderr)
        return 1

    create_status, _, create_body = request(opener, f"{base_url}/AdminPost/Create")
    create_token = extract_antiforgery_token(create_body)
    if create_status != 200 or create_token is None:
        print("F12 could not load the post create form.", file=sys.stderr)
        return 1

    oversized_summary = "s" * 501
    shared_marker = "F12_SHARED_INVALID_INPUT"
    invalid_fields = post_fields(shared_marker, oversized_summary)
    invalid_fields["__RequestVerificationToken"] = create_token
    form_status, form_url, form_body = request(
        opener,
        f"{base_url}/AdminPost/Create",
        data=invalid_fields,
    )

    overpost_marker = "F12 Overpost System Fields"
    overpost_fields = post_fields(overpost_marker, "F12 valid summary")
    overpost_fields.update(
        {
            "Slug": "attacker-controlled-slug",
            "ThumbnailUrl": "https://attacker.invalid/cover.png",
            "ViewCount": "987654",
            "CreatedDate": "2001-01-01T00:00:00Z",
            "__RequestVerificationToken": create_token,
        }
    )
    overpost_status, overpost_url, overpost_body = request(
        opener,
        f"{base_url}/AdminPost/Create",
        data=overpost_fields,
    )

    category_status, _, category_body = request(
        opener,
        f"{base_url}/AdminCategory/Create",
    )
    category_token = extract_antiforgery_token(category_body)
    invalid_category_status = 0
    invalid_category_body = ""
    if category_token is not None:
        invalid_category_status, _, invalid_category_body = request(
            opener,
            f"{base_url}/AdminCategory/Create",
            data={
                "Name": "c" * 101,
                "Slug": "attacker-category-slug",
                "CreatedDate": "2001-01-01T00:00:00Z",
                "__RequestVerificationToken": category_token,
            },
        )

    public_opener, _ = cookie_opener()
    webhook_shared_status, webhook_shared_body = webhook_request(
        public_opener,
        base_url,
        webhook_secret,
        {
            "title": shared_marker,
            "content": "F12 shared validation content",
            "summary": oversized_summary,
            "coverImageUrl": "http://images.example.test/unsafe.png",
            "categoryId": 1001,
            "isPublished": False,
        },
    )
    webhook_inactive_status, webhook_inactive_body = webhook_request(
        public_opener,
        base_url,
        webhook_secret,
        {
            "title": "F12 inactive category",
            "content": "F12 inactive category content",
            "categoryId": 1002,
            "isPublished": False,
        },
    )

    shared_webhook_fields = error_fields(webhook_shared_body)
    inactive_webhook_fields = error_fields(webhook_inactive_body)
    checks = {
        "form_exposes_matching_limits_and_only_active_categories": (
            'maxlength="200"' in create_body
            and 'maxlength="200000"' in create_body
            and 'maxlength="500"' in create_body
            and 'maxlength="1000"' in create_body
            and 'value="1001"' in create_body
            and 'value="1002"' not in create_body
        ),
        "system_fields_are_absent_from_create_contract": all(
            f'name="{field}"' not in create_body
            for field in ("Slug", "ThumbnailUrl", "ViewCount", "CreatedDate")
        ),
        "form_rejects_oversized_summary_without_persisting": (
            form_status == 200
            and "/AdminPost/Create" in form_url
            and "Summary cannot exceed 500 characters." in form_body
            and shared_marker in form_body
            and shared_marker not in overpost_body
        ),
        "webhook_rejects_same_summary_rule_with_field_error": (
            webhook_shared_status == 400
            and "Summary" in shared_webhook_fields
        ),
        "webhook_maps_cover_rule_to_payload_field": (
            webhook_shared_status == 400
            and "CoverImageUrl" in shared_webhook_fields
        ),
        "webhook_rejects_inactive_category": (
            webhook_inactive_status == 400
            and "CategoryId" in inactive_webhook_fields
        ),
        "valid_form_ignores_submitted_system_fields": (
            overpost_status == 200
            and overpost_url.rstrip("/").endswith("/AdminPost")
            and overpost_marker in overpost_body
            and "attacker-controlled-slug" not in overpost_body
            and "https://attacker.invalid/cover.png" not in overpost_body
        ),
        "category_form_enforces_name_limit": (
            category_status == 200
            and category_token is not None
            and 'maxlength="100"' in category_body
            and invalid_category_status == 200
            and "Category name cannot exceed 100 characters." in invalid_category_body
        ),
    }
    statuses = {
        "post_form_invalid": form_status,
        "post_form_overpost_final": overpost_status,
        "category_form_invalid": invalid_category_status,
        "webhook_shared_invalid": webhook_shared_status,
        "webhook_inactive_category": webhook_inactive_status,
    }

    print("F12 shared MVC and webhook validation verification")
    print(json.dumps({"checks": checks, "statuses": statuses}, indent=2))
    failed = [name for name, passed in checks.items() if not passed]
    if failed:
        print(f"F12 verification failed: {', '.join(failed)}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
