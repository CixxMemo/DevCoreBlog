#!/usr/bin/env python3
"""Reproduce baseline findings and the completed F02-F04 security controls."""

from __future__ import annotations

import argparse
import json
import os
import re
import sys

from http_probe_support import (
    cookie_opener,
    extract_antiforgery_token,
    has_authentication_cookie,
    request,
    submit_login,
    wait_until_ready,
)


def extract_markdown_content(body: str) -> str:
    marker = '<div class="markdown-content '
    container_start = body.find(marker)
    if container_start < 0:
        return ""

    content_start = body.find(">", container_start)
    content_end = body.find("</div>", content_start)
    if content_start < 0 or content_end < 0:
        return ""

    return body[content_start + 1 : content_end]


def probe_rejected_password(
    base_url: str,
    username: str,
    password: str | None,
    *,
    include_password: bool,
):
    opener, cookies = cookie_opener()
    get_status, token, status, final_url, body = submit_login(
        opener,
        base_url,
        username,
        password,
        include_password=include_password,
    )
    rejected = (
        get_status == 200
        and token is not None
        and status == 200
        and "/Account/Login" in final_url
        and "Invalid username or password." in body
        and not has_authentication_cookie(cookies)
    )
    authenticated = (
        status == 200
        and "/Admin/Dashboard" in final_url
        and has_authentication_cookie(cookies)
    )
    return rejected, authenticated, status


def build_f04_checks(markdown_body: str) -> dict[str, bool]:
    markdown_content = extract_markdown_content(markdown_body)
    lowered = markdown_content.lower()
    return {
        "raw_html_is_rendered_inert": (
            "&lt;script&gt;window.__f01MarkdownXss=true&lt;/script&gt;"
            in markdown_content
            and "<script" not in lowered
            and "<img" not in lowered
            and "<svg" not in lowered
        ),
        "event_handler_attributes_are_not_active": (
            re.search(
                r"<[^>]+\s(?:onerror|onload|onclick)\s*=",
                markdown_content,
                re.IGNORECASE,
            )
            is None
        ),
        "dangerous_link_and_image_schemes_are_rejected": (
            'href="javascript:' not in lowered
            and 'href="vbscript:' not in lowered
            and 'href="data:' not in lowered
            and 'src="javascript:' not in lowered
            and 'src="vbscript:' not in lowered
            and 'src="data:' not in lowered
        ),
        "generic_attributes_are_not_enabled": (
            "{onclick=&quot;window.__f04AttributeXss=true&quot;}" in markdown_content
        ),
        "approved_youtube_url_uses_fixed_embed": (
            markdown_content.count("<iframe") == 1
            and 'src="https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ"'
            in markdown_content
        ),
        "fake_youtube_host_is_not_embedded": (
            "youtube.com.evil.example" not in lowered
        ),
        "safe_https_and_relative_links_render": (
            '<a href="https://example.com/docs">Safe HTTPS link</a>'
            in markdown_content
            and '<a href="/post/f01-visible">Safe relative link</a>'
            in markdown_content
        ),
        "table_list_and_csharp_code_render": (
            "<table>" in markdown_content
            and "F04_TABLE_MARKER" in markdown_content
            and "<li>F04_LIST_MARKER</li>" in markdown_content
            and '<code class="language-csharp">' in markdown_content
            and "F04_CODE_MARKER" in markdown_content
        ),
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-url", default="http://127.0.0.1:15159")
    parser.add_argument("--wait-seconds", type=int, default=30)
    parser.add_argument("--expect-vulnerable", action="store_true")
    parser.add_argument("--expect-f02-fixed", action="store_true")
    parser.add_argument("--expect-f03-fixed", action="store_true")
    parser.add_argument("--expect-f04-fixed", action="store_true")
    parser.add_argument("--expect-f05-fixed", action="store_true")
    args = parser.parse_args()

    base_url = args.base_url.rstrip("/")
    public_opener, _ = cookie_opener()
    wait_until_ready(
        public_opener,
        f"{base_url}/post/f01-visible",
        "F01_VISIBLE_MARKER",
        args.wait_seconds,
    )

    future_status, _, future_body = request(
        public_opener, f"{base_url}/post/f01-future-visible-marker"
    )
    xss_status, _, xss_body = request(
        public_opener, f"{base_url}/post/f01-markdown-xss"
    )

    admin_username = os.environ.get("DEVCORE_TEST_ADMIN_USERNAME", "f01-admin")
    admin_password = os.environ.get("DEVCORE_TEST_ADMIN_PASSWORD")

    missing_rejected, authenticated_without_password, missing_status = (
        probe_rejected_password(
            base_url,
            admin_username,
            None,
            include_password=False,
        )
    )
    blank_rejected, _, blank_status = probe_rejected_password(
        base_url,
        admin_username,
        "",
        include_password=True,
    )
    whitespace_rejected, _, whitespace_status = probe_rejected_password(
        base_url,
        admin_username,
        "   ",
        include_password=True,
    )

    authenticated_opener, authenticated_cookies = cookie_opener()
    valid_login_status = None
    valid_login_url = ""
    valid_login_succeeded = False
    if admin_password is not None:
        (
            login_get_status,
            login_token,
            valid_login_status,
            valid_login_url,
            _,
        ) = submit_login(
            authenticated_opener,
            base_url,
            admin_username,
            admin_password,
        )
        valid_login_succeeded = (
            login_get_status == 200
            and login_token is not None
            and valid_login_status == 200
            and "/Admin/Dashboard" in valid_login_url
            and has_authentication_cookie(authenticated_cookies)
        )

    toggle_status = None
    toast_status = None
    toast_body = ""
    admin_token = None
    if valid_login_succeeded:
        toast_status, _, toast_body = request(
            authenticated_opener, f"{base_url}/AdminPost"
        )
        admin_token = extract_antiforgery_token(toast_body)
        toggle_status, _, _ = request(
            authenticated_opener,
            f"{base_url}/AdminPost/TogglePublish/2001",
            data={},
        )

    logout_status = None
    post_logout_admin_status = None
    post_logout_admin_url = ""
    if valid_login_succeeded:
        logout_status, _, _ = request(
            authenticated_opener,
            f"{base_url}/Account/Logout",
            data={"__RequestVerificationToken": admin_token or ""},
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
        "missing_password_request_rejected_without_auth_cookie": missing_rejected,
        "blank_password_request_rejected_without_auth_cookie": blank_rejected,
        "whitespace_password_request_rejected_without_auth_cookie": whitespace_rejected,
        "valid_configured_login_succeeds": valid_login_succeeded,
        "valid_logout_clears_session": valid_logout_succeeded,
    }
    f03_checks = {
        "admin_layout_avoids_inner_html_toast_sink": (
            toast_status == 200 and "toast.innerHTML" not in toast_body
        ),
        "admin_layout_uses_text_content_for_toast_message": (
            toast_status == 200 and "messageText.textContent = message" in toast_body
        ),
    }
    f04_checks = build_f04_checks(xss_body)
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
        "f03_checks": f03_checks,
        "f04_checks": f04_checks,
        "remaining_baseline_findings": remaining_findings,
    }
    print(json.dumps(result, indent=2, sort_keys=True))

    if args.expect_vulnerable and not all(remaining_findings.values()):
        print("One or more expected pre-fix findings were not reproduced.", file=sys.stderr)
        return 2
    if args.expect_f02_fixed:
        fixed_findings = {"authentication_without_configured_password"}
        if args.expect_f03_fixed:
            fixed_findings.add("admin_layout_contains_inner_html_toast_sink")
        if args.expect_f04_fixed:
            fixed_findings.add("raw_markdown_script_returned")
        if args.expect_f05_fixed:
            fixed_findings.add("tokenless_publish_toggle_accepted")

        remaining_expected = {
            key: value
            for key, value in remaining_findings.items()
            if key not in fixed_findings
        }
        if not all(f02_checks.values()) or not all(remaining_expected.values()):
            print("F02 or preserved baseline checks did not pass.", file=sys.stderr)
            return 3
    if args.expect_f03_fixed and not all(f03_checks.values()):
        print("F03 toast rendering checks did not pass.", file=sys.stderr)
        return 4
    if args.expect_f04_fixed and not all(f04_checks.values()):
        print("F04 Markdown rendering checks did not pass.", file=sys.stderr)
        return 5
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
