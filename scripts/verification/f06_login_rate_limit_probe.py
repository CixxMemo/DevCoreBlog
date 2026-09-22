#!/usr/bin/env python3
"""Verify the F06 login limiter against the isolated application."""

from __future__ import annotations

import argparse
import json
import os
import sys
import time

from http_probe_support import (
    cookie_opener,
    get_antiforgery_token,
    has_authentication_cookie,
    request,
    request_with_headers,
    submit_login,
    wait_until_ready,
)


def header_value(headers: dict[str, str], name: str) -> str | None:
    lowered_name = name.casefold()
    return next(
        (value for key, value in headers.items() if key.casefold() == lowered_name),
        None,
    )


def post_login(
    opener,
    base_url: str,
    username: str,
    password: str,
    *,
    headers: dict[str, str] | None = None,
):
    get_status, token, _ = get_antiforgery_token(
        opener,
        f"{base_url}/Account/Login",
    )
    status, final_url, body, response_headers = request_with_headers(
        opener,
        f"{base_url}/Account/Login",
        data={
            "username": username,
            "password": password,
            "__RequestVerificationToken": token or "",
        },
        headers=headers,
    )
    return get_status, token, status, final_url, body, response_headers


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-url", default="http://127.0.0.1:15159")
    parser.add_argument("--wait-seconds", type=int, default=30)
    parser.add_argument("--permit-limit", type=int, default=4)
    parser.add_argument("--window-seconds", type=int, default=2)
    args = parser.parse_args()

    base_url = args.base_url.rstrip("/")
    admin_username = os.environ.get("DEVCORE_TEST_ADMIN_USERNAME", "f06-admin")
    admin_password = os.environ.get("DEVCORE_TEST_ADMIN_PASSWORD")
    username_marker = os.environ.get(
        "DEVCORE_F06_USERNAME_MARKER",
        "F06_USERNAME_MUST_NOT_BE_LOGGED",
    )
    password_marker = os.environ.get(
        "DEVCORE_F06_PASSWORD_MARKER",
        "F06_PASSWORD_MUST_NOT_BE_LOGGED",
    )

    if not admin_password:
        print("DEVCORE_TEST_ADMIN_PASSWORD is required.", file=sys.stderr)
        return 2
    if args.permit_limit < 1 or args.window_seconds < 1:
        print("Permit limit and window must be positive.", file=sys.stderr)
        return 2

    opener, cookies = cookie_opener()
    wait_until_ready(
        opener,
        f"{base_url}/post/f01-visible",
        "F01_VISIBLE_MARKER",
        args.wait_seconds,
    )

    allowed_statuses: list[int] = []
    allowed_bodies: list[str] = []
    for attempt in range(args.permit_limit):
        attempt_username = (
            username_marker if attempt == 0 else f"f06-distinct-user-{attempt}"
        )
        _, _, status, _, body, _ = post_login(
            opener,
            base_url,
            attempt_username,
            password_marker,
        )
        allowed_statuses.append(status)
        allowed_bodies.append(body)

    (
        blocked_get_status,
        blocked_token,
        blocked_status,
        _,
        blocked_body,
        blocked_headers,
    ) = post_login(
        opener,
        base_url,
        "f06-another-distinct-user",
        password_marker,
    )
    retry_after_raw = header_value(blocked_headers, "Retry-After")
    try:
        retry_after_seconds = int(retry_after_raw or "")
    except ValueError:
        retry_after_seconds = 0
    authenticated_while_blocked = has_authentication_cookie(cookies)

    _, _, spoofed_status, _, _, _ = post_login(
        opener,
        base_url,
        "f06-forwarded-header-user",
        password_marker,
        headers={"X-Forwarded-For": "203.0.113.201"},
    )

    public_status, _, public_body = request(
        opener,
        f"{base_url}/post/f01-visible",
    )
    login_get_status, _, login_get_body = request(
        opener,
        f"{base_url}/Account/Login",
    )

    time.sleep(max(args.window_seconds, retry_after_seconds) + 0.5)
    (
        recovery_get_status,
        recovery_token,
        recovery_status,
        recovery_url,
        _,
    ) = submit_login(
        opener,
        base_url,
        admin_username,
        admin_password,
    )

    checks = {
        "configured_attempts_reach_generic_failure_view": (
            len(allowed_statuses) == args.permit_limit
            and all(status == 200 for status in allowed_statuses)
            and all(
                "Invalid username or password." in body
                for body in allowed_bodies
            )
        ),
        "next_attempt_is_rejected_with_429": (
            blocked_get_status == 200
            and blocked_token is not None
            and blocked_status == 429
            and not authenticated_while_blocked
        ),
        "rejection_has_english_wait_message_and_retry_after": (
            "Too many sign-in attempts." in blocked_body
            and "Try again in" in blocked_body
            and retry_after_seconds >= 1
        ),
        "username_input_does_not_create_rate_limit_partitions": (
            blocked_status == 429
        ),
        "untrusted_forwarded_ip_does_not_bypass_limit": spoofed_status == 429,
        "public_pages_and_login_get_remain_available": (
            public_status == 200
            and "F01_VISIBLE_MARKER" in public_body
            and login_get_status == 200
            and "Admin Sign In" in login_get_body
        ),
        "valid_login_recovers_after_window": (
            recovery_get_status == 200
            and recovery_token is not None
            and recovery_status == 200
            and "/Admin/Dashboard" in recovery_url
            and has_authentication_cookie(cookies)
        ),
    }
    statuses = {
        "allowed_invalid_attempts": allowed_statuses,
        "blocked_login_get": blocked_get_status,
        "blocked_antiforgery_token_present": blocked_token is not None,
        "authentication_cookie_after_block": authenticated_while_blocked,
        "blocked_attempt": blocked_status,
        "spoofed_forwarded_ip_attempt": spoofed_status,
        "retry_after_seconds": retry_after_seconds,
        "public_page_while_blocked": public_status,
        "login_get_while_blocked": login_get_status,
        "valid_login_after_window": recovery_status,
    }

    print("F06 isolated login rate-limit verification")
    print(json.dumps({"checks": checks, "statuses": statuses}, indent=2))

    failed = [name for name, passed in checks.items() if not passed]
    if failed:
        print(f"F06 verification failed: {', '.join(failed)}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
