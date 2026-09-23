#!/usr/bin/env python3
"""Verify F13 script ordering and the JavaScript-free post form baseline."""

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


SCRIPT_PATHS = (
    "/lib/jquery/dist/jquery.min.js",
    "/lib/jquery-validation/dist/jquery.validate.min.js",
    "/lib/jquery-validation-unobtrusive/dist/jquery.validate.unobtrusive.min.js",
)


def script_sources(body: str) -> list[str]:
    return re.findall(r'<script\b[^>]*\bsrc="([^"]+)"', body, re.IGNORECASE)


def has_single_ordered_validation_chain(body: str) -> bool:
    sources = script_sources(body)
    positions: list[int] = []
    for path in SCRIPT_PATHS:
        matching = [index for index, source in enumerate(sources) if source.startswith(path)]
        if len(matching) != 1:
            return False
        positions.append(matching[0])
    return positions == sorted(positions)


def content_textarea(body: str) -> str:
    match = re.search(
        r'<textarea\b(?=[^>]*\bid="Content")[^>]*>',
        body,
        re.IGNORECASE,
    )
    return match.group(0) if match else ""


def post_fields(title: str, content: str) -> dict[str, str]:
    return {
        "Title": title,
        "Content": content,
        "CategoryId": "1001",
        "Summary": "F13 JavaScript-free form summary",
        "Excerpt": "F13 JavaScript-free form excerpt",
        "IsPublished": "false",
        "IsActive": "true",
        "PublishDate": "2026-09-23T12:00",
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-url", default="http://127.0.0.1:15159")
    parser.add_argument("--wait-seconds", type=int, default=30)
    args = parser.parse_args()

    base_url = args.base_url.rstrip("/")
    username = os.environ.get("DEVCORE_TEST_ADMIN_USERNAME", "f13-admin")
    password = os.environ.get("DEVCORE_TEST_ADMIN_PASSWORD", "")
    if not password:
        print("DEVCORE_TEST_ADMIN_PASSWORD is required.", file=sys.stderr)
        return 2

    opener, cookies = cookie_opener()
    wait_until_ready(
        opener,
        f"{base_url}/Account/Login",
        "Admin Sign In",
        args.wait_seconds,
    )
    _, login_token, login_status, login_url, login_body = submit_login(
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
        print("F13 could not establish an authenticated fixture session.", file=sys.stderr)
        return 1

    pages: dict[str, str] = {}
    page_statuses: dict[str, int] = {}
    for name, path in (
        ("post_create", "/AdminPost/Create"),
        ("post_edit", "/AdminPost/Edit/2003"),
        ("category_create", "/AdminCategory/Create"),
        ("category_edit", "/AdminCategory/Edit/1001"),
    ):
        status, _, body = request(opener, f"{base_url}{path}")
        page_statuses[name] = status
        pages[name] = body

    public_opener, _ = cookie_opener()
    login_page_status, _, public_login_body = request(
        public_opener,
        f"{base_url}/Account/Login",
    )

    create_body = pages["post_create"]
    invalid_token = extract_antiforgery_token(create_body)
    invalid_status = 0
    invalid_body = ""
    if invalid_token is not None:
        invalid_fields = post_fields("", "")
        invalid_fields["__RequestVerificationToken"] = invalid_token
        invalid_status, _, invalid_body = request(
            opener,
            f"{base_url}/AdminPost/Create",
            data=invalid_fields,
        )

    fresh_status, _, fresh_create_body = request(opener, f"{base_url}/AdminPost/Create")
    save_token = extract_antiforgery_token(fresh_create_body)
    save_status = 0
    save_url = ""
    save_body = ""
    marker = "F13 JAVASCRIPT FREE SAVE"
    if save_token is not None:
        save_fields = post_fields(marker, "F13 JavaScript-free Markdown content")
        save_fields["__RequestVerificationToken"] = save_token
        save_status, save_url, save_body = request(
            opener,
            f"{base_url}/AdminPost/Create",
            data=save_fields,
        )

    textarea_tag = content_textarea(create_body)
    checks = {
        "all_form_pages_load_one_ordered_local_validation_chain": (
            all(status == 200 for status in page_statuses.values())
            and all(has_single_ordered_validation_chain(body) for body in pages.values())
        ),
        "post_pages_load_shared_editor_helper_once": all(
            sum(
                source.startswith("/js/admin-post-editor.js")
                for source in script_sources(pages[name])
            )
            == 1
            for name in ("post_create", "post_edit")
        ),
        "login_does_not_load_form_plugins": (
            login_page_status == 200
            and all(path not in public_login_body for path in SCRIPT_PATHS)
            and all(path not in login_body for path in SCRIPT_PATHS)
        ),
        "markdown_textarea_is_visible_without_javascript": (
            '<div id="editor" hidden>' in create_body
            and 'id="content-fallback"' in create_body
            and bool(textarea_tag)
            and 'data-validate-hidden="true"' in textarea_tag
            and not re.search(
                r'(?<![-\w])hidden(?:\s|=|>)',
                textarea_tag,
                re.IGNORECASE,
            )
            and not re.search(r'class="[^"]*\bhidden\b', textarea_tag, re.IGNORECASE)
        ),
        "client_and_server_required_messages_match": (
            'data-val-required="Title is required."' in create_body
            and 'data-val-required="Content is required."' in create_body
            and invalid_status == 200
            and "Title is required." in invalid_body
            and "Content is required." in invalid_body
        ),
        "post_can_be_saved_without_javascript": (
            fresh_status == 200
            and save_token is not None
            and save_status == 200
            and save_url.rstrip("/").endswith("/AdminPost")
            and marker in save_body
        ),
    }
    statuses = {
        **page_statuses,
        "login": login_page_status,
        "invalid_post": invalid_status,
        "javascript_free_save_final": save_status,
    }

    print("F13 frontend validation and JavaScript-free form verification")
    print(json.dumps({"checks": checks, "statuses": statuses}, indent=2))
    failed = [name for name, passed in checks.items() if not passed]
    if failed:
        print(f"F13 verification failed: {', '.join(failed)}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
