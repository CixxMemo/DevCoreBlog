#!/usr/bin/env python3
"""Verify the F05 antiforgery policy against the isolated application."""

from __future__ import annotations

import argparse
import json
import os
import sys

from http_probe_support import (
    cookie_opener,
    extract_antiforgery_token,
    get_antiforgery_token,
    has_authentication_cookie,
    request,
    submit_login,
    wait_until_ready,
)


def probe_login_token_rejections(
    base_url: str,
    username: str,
    password: str,
) -> tuple[dict[str, bool], dict[str, int]]:
    tokenless_opener, tokenless_cookies = cookie_opener()
    tokenless_status, _, _ = request(
        tokenless_opener,
        f"{base_url}/Account/Login",
        data={"username": username, "password": password},
    )

    invalid_opener, invalid_cookies = cookie_opener()
    get_antiforgery_token(invalid_opener, f"{base_url}/Account/Login")
    invalid_status, _, _ = request(
        invalid_opener,
        f"{base_url}/Account/Login",
        data={
            "username": username,
            "password": password,
            "__RequestVerificationToken": "invalid-f05-token",
        },
    )

    checks = {
        "login_rejects_missing_token_with_400": (
            tokenless_status == 400
            and not has_authentication_cookie(tokenless_cookies)
        ),
        "login_rejects_invalid_token_with_400": (
            invalid_status == 400
            and not has_authentication_cookie(invalid_cookies)
        ),
    }
    return checks, {
        "tokenless_login": tokenless_status,
        "invalid_token_login": invalid_status,
    }


def probe_tokenless_protected_routes(opener, base_url: str) -> dict[str, int]:
    routes = {
        "logout": "/Account/Logout",
        "toggle_publish": "/AdminPost/TogglePublish/2001",
        "post_create": "/AdminPost/Create",
        "post_edit": "/AdminPost/Edit/2001",
        "post_delete": "/AdminPost/Delete/2008",
        "editor_upload": "/AdminPost/UploadEditorImage",
        "legacy_upload": "/AdminPost/UploadImage",
        "category_create": "/AdminCategory/Create",
        "category_edit": "/AdminCategory/Edit/1001",
        "category_delete": "/AdminCategory/Delete/1002",
    }
    statuses = {}
    for name, path in routes.items():
        status, _, _ = request(opener, f"{base_url}{path}", data={})
        statuses[name] = status
    return statuses


def probe_form_rendering(opener, base_url: str, admin_index_body: str) -> dict[str, bool]:
    checks = {
        "admin_post_index": extract_antiforgery_token(admin_index_body) is not None,
    }
    bodies = {"admin_post_index": admin_index_body}
    pages = {
        "post_create": "/AdminPost/Create",
        "post_edit": "/AdminPost/Edit/2001",
        "category_index": "/AdminCategory",
        "category_create": "/AdminCategory/Create",
        "category_edit": "/AdminCategory/Edit/1001",
    }
    for name, path in pages.items():
        status, _, body = request(opener, f"{base_url}{path}")
        checks[name] = status == 200 and extract_antiforgery_token(body) is not None
        bodies[name] = body

    checks["ajax_views_send_header"] = all(
        "'X-CSRF-TOKEN': antiforgeryToken" in bodies[name]
        for name in ("admin_post_index", "post_create", "post_edit")
    )
    return checks


def probe_webhook(
    authenticated_opener,
    public_opener,
    base_url: str,
    webhook_secret: str,
) -> tuple[dict[str, bool], dict[str, int]]:
    payload = json.dumps(
        {
            "title": "F05 Webhook Draft",
            "content": "F05_WEBHOOK_MARKER",
            "categoryId": 1001,
            "isPublished": False,
        }
    ).encode()
    content_header = {"Content-Type": "application/json"}
    cookie_without_secret, _, _ = request(
        authenticated_opener,
        f"{base_url}/api/webhooks/posts",
        raw_data=payload,
        headers=content_header,
    )
    invalid_secret, _, _ = request(
        public_opener,
        f"{base_url}/api/webhooks/posts",
        raw_data=payload,
        headers={
            "Content-Type": "application/json",
            "X-DevCore-Secret": "invalid-f05-secret",
        },
    )
    valid_secret, _, _ = request(
        public_opener,
        f"{base_url}/api/webhooks/posts",
        raw_data=payload,
        headers={
            "Content-Type": "application/json",
            "X-DevCore-Secret": webhook_secret,
        },
    )
    checks = {
        "webhook_requires_secret_even_with_admin_cookie": cookie_without_secret == 401,
        "webhook_secret_auth_bypasses_only_antiforgery": (
            invalid_secret == 401 and valid_secret == 200
        ),
    }
    return checks, {
        "webhook_cookie_without_secret": cookie_without_secret,
        "webhook_invalid_secret": invalid_secret,
        "webhook_valid_secret": valid_secret,
    }


def probe_authenticated_flow(
    base_url: str,
    username: str,
    password: str,
    webhook_secret: str,
    public_opener,
) -> tuple[dict[str, bool], dict[str, object]]:
    opener, cookies = cookie_opener()
    get_status, login_token, login_status, login_url, _ = submit_login(
        opener,
        base_url,
        username,
        password,
    )
    login_succeeded = (
        get_status == 200
        and login_token is not None
        and login_status == 200
        and "/Admin/Dashboard" in login_url
        and has_authentication_cookie(cookies)
    )
    if not login_succeeded:
        return {"valid_token_login_toggle_and_logout_succeed": False}, {
            "valid_login_final": login_status,
        }

    admin_status, _, admin_body = request(opener, f"{base_url}/AdminPost")
    admin_token = extract_antiforgery_token(admin_body)
    form_checks = probe_form_rendering(opener, base_url, admin_body)
    protected_statuses = probe_tokenless_protected_routes(opener, base_url)

    invalid_toggle, _, _ = request(
        opener,
        f"{base_url}/AdminPost/TogglePublish/2001",
        data={},
        headers={"X-CSRF-TOKEN": "invalid-f05-token"},
    )
    valid_toggle, _, valid_toggle_body = request(
        opener,
        f"{base_url}/AdminPost/TogglePublish/2001",
        data={},
        headers={"X-CSRF-TOKEN": admin_token or ""},
    )
    valid_upload, _, valid_upload_body = request(
        opener,
        f"{base_url}/AdminPost/UploadEditorImage",
        data={},
        headers={"X-CSRF-TOKEN": admin_token or ""},
    )

    valid_toggle_succeeded = False
    if valid_toggle == 200:
        try:
            valid_toggle_succeeded = json.loads(valid_toggle_body).get("success") is True
        except json.JSONDecodeError:
            pass

    webhook_checks, webhook_statuses = probe_webhook(
        opener,
        public_opener,
        base_url,
        webhook_secret,
    )
    after_rejected_logout, after_rejected_logout_url, _ = request(
        opener, f"{base_url}/Admin/Dashboard"
    )
    rejected_logout_preserved_session = (
        after_rejected_logout == 200
        and "/Admin/Dashboard" in after_rejected_logout_url
        and has_authentication_cookie(cookies)
    )

    logout_status, _, _ = request(
        opener,
        f"{base_url}/Account/Logout",
        data={"__RequestVerificationToken": admin_token or ""},
    )
    post_logout_status, post_logout_url, _ = request(
        opener, f"{base_url}/Admin/Dashboard"
    )
    valid_logout = (
        logout_status == 200
        and post_logout_status == 200
        and "/Account/Login" in post_logout_url
        and not has_authentication_cookie(cookies)
    )

    checks = {
        "all_cookie_post_endpoints_reject_missing_token_with_400": (
            len(protected_statuses) == 10
            and all(status == 400 for status in protected_statuses.values())
        ),
        "invalid_ajax_token_is_rejected_with_400": invalid_toggle == 400,
        "razor_forms_render_tokens_and_ajax_sends_header": (
            len(form_checks) == 7 and all(form_checks.values())
        ),
        "valid_token_login_toggle_and_logout_succeed": (
            valid_toggle_succeeded and valid_logout
        ),
        "valid_token_reaches_upload_action": (
            valid_upload == 400 and "No image file provided." in valid_upload_body
        ),
        "rejected_logout_preserves_authenticated_session": (
            protected_statuses.get("logout") == 400
            and rejected_logout_preserved_session
        ),
        **webhook_checks,
    }
    statuses: dict[str, object] = {
        "valid_login_final": login_status,
        "admin_index": admin_status,
        "tokenless_cookie_posts": protected_statuses,
        "invalid_token_toggle": invalid_toggle,
        "valid_token_toggle": valid_toggle,
        "valid_token_upload_probe": valid_upload,
        "logout_final": logout_status,
        "post_logout_admin_final": post_logout_status,
        **webhook_statuses,
    }
    return checks, statuses


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-url", default="http://127.0.0.1:15159")
    parser.add_argument("--wait-seconds", type=int, default=30)
    args = parser.parse_args()

    base_url = args.base_url.rstrip("/")
    username = os.environ.get("DEVCORE_TEST_ADMIN_USERNAME", "f01-admin")
    password = os.environ.get("DEVCORE_TEST_ADMIN_PASSWORD", "")
    webhook_secret = os.environ.get("DEVCORE_TEST_WEBHOOK_SECRET", "")
    public_opener, _ = cookie_opener()
    wait_until_ready(
        public_opener,
        f"{base_url}/post/f01-visible",
        "F01_VISIBLE_MARKER",
        args.wait_seconds,
    )

    login_checks, login_statuses = probe_login_token_rejections(
        base_url,
        username,
        password,
    )
    flow_checks, flow_statuses = probe_authenticated_flow(
        base_url,
        username,
        password,
        webhook_secret,
        public_opener,
    )
    anonymous_status, anonymous_url, _ = request(
        public_opener,
        f"{base_url}/AdminPost/TogglePublish/2001",
        data={},
    )
    checks = {
        **login_checks,
        **flow_checks,
        "anonymous_write_is_rejected": (
            anonymous_status in {400, 401, 403}
            or "/Account/Login" in anonymous_url
        ),
    }
    result = {
        "f05_checks": checks,
        "statuses": {
            **login_statuses,
            **flow_statuses,
            "anonymous_toggle_final": anonymous_status,
        },
    }
    print(json.dumps(result, indent=2, sort_keys=True))

    if not all(checks.values()):
        print("F05 antiforgery checks did not pass.", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
