#!/usr/bin/env python3
"""Reproduce selected pre-fix findings against the isolated F01 server."""

from __future__ import annotations

import argparse
import http.cookiejar
import json
import os
import sys
import time
import urllib.error
import urllib.parse
import urllib.request


def request(opener, url: str, *, data: dict[str, str] | None = None):
    encoded = urllib.parse.urlencode(data).encode() if data is not None else None
    method = "POST" if data is not None else "GET"
    req = urllib.request.Request(url, data=encoded, method=method)
    try:
        with opener.open(req, timeout=5) as response:
            return response.status, response.geturl(), response.read().decode("utf-8", "replace")
    except urllib.error.HTTPError as error:
        return error.code, error.geturl(), error.read().decode("utf-8", "replace")


def has_authentication_cookie(cookies: http.cookiejar.CookieJar) -> bool:
    return any(cookie.name == ".AspNetCore.Cookies" for cookie in cookies)


def wait_until_ready(opener, base_url: str, timeout_seconds: int) -> None:
    deadline = time.monotonic() + timeout_seconds
    last_error = "server did not respond"
    while time.monotonic() < deadline:
        try:
            status, _, body = request(opener, f"{base_url}/post/f01-visible")
            if status == 200 and "F01_VISIBLE_MARKER" in body:
                return
            last_error = f"status={status}, visible_marker={('F01_VISIBLE_MARKER' in body)}"
        except (OSError, urllib.error.URLError) as error:
            last_error = str(error)
        time.sleep(0.25)
    raise RuntimeError(f"F01 server was not ready: {last_error}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-url", default="http://127.0.0.1:15159")
    parser.add_argument("--wait-seconds", type=int, default=30)
    parser.add_argument("--expect-vulnerable", action="store_true")
    parser.add_argument("--expect-f02-fixed", action="store_true")
    args = parser.parse_args()

    base_url = args.base_url.rstrip("/")
    public_cookies = http.cookiejar.CookieJar()
    public_opener = urllib.request.build_opener(
        urllib.request.HTTPCookieProcessor(public_cookies)
    )
    wait_until_ready(public_opener, base_url, args.wait_seconds)

    future_status, _, future_body = request(
        public_opener, f"{base_url}/post/f01-future-visible-marker"
    )
    xss_status, _, xss_body = request(
        public_opener, f"{base_url}/post/f01-markdown-xss"
    )

    admin_username = os.environ.get("DEVCORE_TEST_ADMIN_USERNAME", "f01-admin")
    admin_password = os.environ.get("DEVCORE_TEST_ADMIN_PASSWORD")

    missing_cookies = http.cookiejar.CookieJar()
    missing_opener = urllib.request.build_opener(
        urllib.request.HTTPCookieProcessor(missing_cookies)
    )
    missing_status, missing_url, missing_body = request(
        missing_opener,
        f"{base_url}/Account/Login",
        data={"username": admin_username},
    )
    missing_password_rejected = (
        missing_status == 200
        and "/Account/Login" in missing_url
        and "Invalid username or password." in missing_body
        and not has_authentication_cookie(missing_cookies)
    )

    blank_cookies = http.cookiejar.CookieJar()
    blank_opener = urllib.request.build_opener(
        urllib.request.HTTPCookieProcessor(blank_cookies)
    )
    blank_status, blank_url, blank_body = request(
        blank_opener,
        f"{base_url}/Account/Login",
        data={"username": admin_username, "password": ""},
    )
    blank_password_rejected = (
        blank_status == 200
        and "/Account/Login" in blank_url
        and "Invalid username or password." in blank_body
        and not has_authentication_cookie(blank_cookies)
    )

    whitespace_cookies = http.cookiejar.CookieJar()
    whitespace_opener = urllib.request.build_opener(
        urllib.request.HTTPCookieProcessor(whitespace_cookies)
    )
    whitespace_status, whitespace_url, whitespace_body = request(
        whitespace_opener,
        f"{base_url}/Account/Login",
        data={"username": admin_username, "password": "   "},
    )
    whitespace_password_rejected = (
        whitespace_status == 200
        and "/Account/Login" in whitespace_url
        and "Invalid username or password." in whitespace_body
        and not has_authentication_cookie(whitespace_cookies)
    )

    authenticated_cookies = http.cookiejar.CookieJar()
    authenticated_opener = urllib.request.build_opener(
        urllib.request.HTTPCookieProcessor(authenticated_cookies)
    )
    valid_login_status = None
    valid_login_url = ""
    if admin_password is not None:
        valid_login_status, valid_login_url, _ = request(
            authenticated_opener,
            f"{base_url}/Account/Login",
            data={"username": admin_username, "password": admin_password},
        )
    valid_login_succeeded = (
        valid_login_status == 200
        and "/Admin/Dashboard" in valid_login_url
        and has_authentication_cookie(authenticated_cookies)
    )

    authenticated_without_password = (
        missing_status == 200
        and "/Admin/Dashboard" in missing_url
        and has_authentication_cookie(missing_cookies)
    )
    toggle_status = None
    toast_status = None
    toast_body = ""
    if valid_login_succeeded:
        toggle_status, _, _ = request(
            authenticated_opener,
            f"{base_url}/AdminPost/TogglePublish/2001",
            data={},
        )
        toast_status, _, toast_body = request(
            authenticated_opener, f"{base_url}/AdminPost"
        )

    logout_status = None
    post_logout_admin_status = None
    post_logout_admin_url = ""
    if valid_login_succeeded:
        logout_status, _, _ = request(
            authenticated_opener,
            f"{base_url}/Account/Logout",
            data={},
        )
        post_logout_admin_status, post_logout_admin_url, _ = request(
            authenticated_opener, f"{base_url}/Admin/Dashboard"
        )
    valid_logout_succeeded = (
        logout_status == 200
        and post_logout_admin_status == 200
        and "/Account/Login" in post_logout_admin_url
        and not has_authentication_cookie(authenticated_cookies)
    )

    remaining_findings = {
        "authentication_without_configured_password": authenticated_without_password,
        "future_post_publicly_visible": (
            future_status == 200 and "F01_FUTURE_VISIBLE_MARKER" in future_body
        ),
        "raw_markdown_script_returned": (
            xss_status == 200
            and '<script>window.__f01MarkdownXss=true</script>' in xss_body
        ),
        "tokenless_publish_toggle_accepted": toggle_status == 200,
        "admin_layout_contains_inner_html_toast_sink": (
            toast_status == 200 and "toast.innerHTML" in toast_body
        ),
    }
    f02_checks = {
        "missing_password_request_rejected_without_auth_cookie": missing_password_rejected,
        "blank_password_request_rejected_without_auth_cookie": blank_password_rejected,
        "whitespace_password_request_rejected_without_auth_cookie": whitespace_password_rejected,
        "valid_configured_login_succeeds": valid_login_succeeded,
        "valid_logout_clears_session": valid_logout_succeeded,
    }
    result = {
        "fixture_visible": True,
        "statuses": {
            "future_detail": future_status,
            "markdown_detail": xss_status,
            "missing_password_login_final": missing_status,
            "blank_password_login_final": blank_status,
            "whitespace_password_login_final": whitespace_status,
            "valid_login_final": valid_login_status,
            "tokenless_toggle": toggle_status,
            "admin_index": toast_status,
            "logout_final": logout_status,
            "post_logout_admin_final": post_logout_admin_status,
        },
        "f02_checks": f02_checks,
        "remaining_baseline_findings": remaining_findings,
    }
    print(json.dumps(result, indent=2, sort_keys=True))

    if args.expect_vulnerable and not all(remaining_findings.values()):
        print("One or more expected pre-fix findings were not reproduced.", file=sys.stderr)
        return 2
    if args.expect_f02_fixed:
        remaining_expected = {
            key: value
            for key, value in remaining_findings.items()
            if key != "authentication_without_configured_password"
        }
        if not all(f02_checks.values()) or not all(remaining_expected.values()):
            print("F02 or preserved baseline checks did not pass.", file=sys.stderr)
            return 3
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
