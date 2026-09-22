#!/usr/bin/env python3
"""Verify the F08 cookie policy, expiry, restart, and revocation behavior."""

from __future__ import annotations

import argparse
import http.cookiejar
import json
import os
import sys
import time
import urllib.request

from http_probe_support import (
    AUTHENTICATION_COOKIE_NAME,
    cookie_opener,
    has_authentication_cookie,
    request,
    submit_login,
    wait_until_ready,
)


def persisted_opener(cookie_file: str, *, allow_untrusted_https: bool):
    cookies = http.cookiejar.MozillaCookieJar(cookie_file)
    cookies.load(ignore_discard=True, ignore_expires=True)
    handlers = [urllib.request.HTTPCookieProcessor(cookies)]
    if allow_untrusted_https:
        import ssl

        context = ssl.create_default_context()
        context.check_hostname = False
        context.verify_mode = ssl.CERT_NONE
        handlers.append(urllib.request.HTTPSHandler(context=context))
    return urllib.request.build_opener(*handlers), cookies


def authentication_cookie(cookies: http.cookiejar.CookieJar):
    return next(
        (cookie for cookie in cookies if cookie.name == AUTHENTICATION_COOKIE_NAME),
        None,
    )


def cookie_rest_value(cookie, name: str) -> str | None:
    return next(
        (str(value) for key, value in cookie._rest.items() if key.casefold() == name.casefold()),
        None,
    )


def login(opener, base_url: str, username: str, password: str):
    return submit_login(opener, base_url, username, password)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--mode",
        required=True,
        choices=("issue", "current", "stale", "expiry"),
    )
    parser.add_argument("--base-url", required=True)
    parser.add_argument("--cookie-file")
    parser.add_argument("--lifetime-seconds", type=int, default=4)
    parser.add_argument("--expect-secure", action="store_true")
    parser.add_argument("--wait-seconds", type=int, default=30)
    args = parser.parse_args()

    base_url = args.base_url.rstrip("/")
    allow_untrusted_https = base_url.startswith("https://")
    username = os.environ.get("DEVCORE_TEST_ADMIN_USERNAME", "f08-admin")
    password = os.environ.get("DEVCORE_TEST_ADMIN_PASSWORD", "")
    if not password:
        print("DEVCORE_TEST_ADMIN_PASSWORD is required.", file=sys.stderr)
        return 2

    if args.mode in {"current", "stale"}:
        if not args.cookie_file:
            print("--cookie-file is required for restart checks.", file=sys.stderr)
            return 2
        opener, cookies = persisted_opener(
            args.cookie_file,
            allow_untrusted_https=allow_untrusted_https,
        )
    else:
        opener, cookies = cookie_opener(
            allow_untrusted_https=allow_untrusted_https,
        )

    ready_opener, _ = cookie_opener(
        allow_untrusted_https=allow_untrusted_https,
    )
    wait_until_ready(
        ready_opener,
        f"{base_url}/Account/Login",
        "Admin Sign In",
        args.wait_seconds,
    )

    checks: dict[str, bool] = {}
    statuses: dict[str, object] = {}

    if args.mode == "issue":
        get_status, token, status, final_url, _ = login(
            opener,
            base_url,
            username,
            password,
        )
        cookie = authentication_cookie(cookies)
        checks = {
            "login_issues_admin_session": (
                get_status == 200
                and token is not None
                and status == 200
                and "/Admin/Dashboard" in final_url
                and cookie is not None
            ),
            "cookie_is_http_only": (
                cookie is not None
                and cookie_rest_value(cookie, "HttpOnly") is not None
            ),
            "cookie_uses_lax_same_site": (
                cookie is not None
                and (cookie_rest_value(cookie, "SameSite") or "").casefold() == "lax"
            ),
            "cookie_has_root_path": cookie is not None and cookie.path == "/",
            "cookie_secure_policy_matches_fixture": (
                cookie is not None and cookie.secure is args.expect_secure
            ),
            "browser_cookie_is_non_persistent": (
                cookie is not None and cookie.discard and cookie.expires is None
            ),
        }
        statuses = {
            "login_get": get_status,
            "login_final": status,
            "login_url": final_url,
            "secure": None if cookie is None else cookie.secure,
            "same_site": None if cookie is None else cookie_rest_value(cookie, "SameSite"),
            "session_cookie": None if cookie is None else cookie.discard,
        }
        if args.cookie_file and all(checks.values()):
            persistent_cookies = http.cookiejar.MozillaCookieJar(args.cookie_file)
            for item in cookies:
                persistent_cookies.set_cookie(item)
            persistent_cookies.save(ignore_discard=True, ignore_expires=True)

    elif args.mode == "current":
        status, final_url, _ = request(opener, f"{base_url}/Admin/Dashboard")
        checks = {
            "same_version_cookie_survives_restart": (
                status == 200
                and "/Admin/Dashboard" in final_url
                and has_authentication_cookie(cookies)
            )
        }
        statuses = {"admin_final": status, "admin_url": final_url}

    elif args.mode == "stale":
        status, final_url, _ = request(opener, f"{base_url}/Admin/Dashboard")
        stale_rejected = (
            status == 200
            and "/Account/Login" in final_url
            and not has_authentication_cookie(cookies)
        )
        fresh_opener, fresh_cookies = cookie_opener(
            allow_untrusted_https=allow_untrusted_https,
        )
        get_status, token, fresh_status, fresh_url, _ = login(
            fresh_opener,
            base_url,
            username,
            password,
        )
        checks = {
            "changed_session_version_rejects_old_cookie": stale_rejected,
            "changed_session_version_allows_fresh_login": (
                get_status == 200
                and token is not None
                and fresh_status == 200
                and "/Admin/Dashboard" in fresh_url
                and has_authentication_cookie(fresh_cookies)
            ),
        }
        statuses = {
            "stale_cookie_final": status,
            "stale_cookie_url": final_url,
            "fresh_login_final": fresh_status,
        }

    else:
        get_status, token, login_status, login_url, _ = login(
            opener,
            base_url,
            username,
            password,
        )
        cookie = authentication_cookie(cookies)
        time.sleep(args.lifetime_seconds * 0.6)
        midpoint_status, midpoint_url, _ = request(
            opener,
            f"{base_url}/Admin/Dashboard",
        )
        time.sleep(args.lifetime_seconds * 0.6)
        expired_status, expired_url, _ = request(
            opener,
            f"{base_url}/Admin/Dashboard",
        )
        checks = {
            "https_login_issues_secure_http_only_lax_cookie": (
                get_status == 200
                and token is not None
                and login_status == 200
                and "/Admin/Dashboard" in login_url
                and cookie is not None
                and cookie.secure
                and cookie_rest_value(cookie, "HttpOnly") is not None
                and (cookie_rest_value(cookie, "SameSite") or "").casefold() == "lax"
            ),
            "session_remains_valid_past_half_life": (
                midpoint_status == 200 and "/Admin/Dashboard" in midpoint_url
            ),
            "session_expires_at_fixed_boundary_without_sliding_renewal": (
                expired_status == 200 and "/Account/Login" in expired_url
            ),
        }
        statuses = {
            "login_final": login_status,
            "midpoint_final": midpoint_status,
            "midpoint_url": midpoint_url,
            "expired_final": expired_status,
            "expired_url": expired_url,
            "tested_lifetime_seconds": args.lifetime_seconds,
        }

    print(f"F08 isolated session verification ({args.mode})")
    print(json.dumps({"checks": checks, "statuses": statuses}, indent=2))
    failed = [name for name, passed in checks.items() if not passed]
    if failed:
        print(f"F08 verification failed: {', '.join(failed)}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
