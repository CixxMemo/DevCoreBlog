#!/usr/bin/env python3
"""Verify every F09 HTTP upload surface against the shared image policy."""

from __future__ import annotations

import argparse
import json
import os
import sys

from http_probe_support import (
    cookie_opener,
    extract_antiforgery_token,
    has_authentication_cookie,
    post_file,
    request,
    submit_login,
    wait_until_ready,
)


MAXIMUM_FILE_BYTES = 8 * 1024 * 1024


def json_message(body: str) -> str:
    try:
        value = json.loads(body)
    except json.JSONDecodeError:
        return ""
    return value.get("message", "") if isinstance(value, dict) else ""


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-url", default="http://127.0.0.1:15159")
    parser.add_argument("--wait-seconds", type=int, default=30)
    args = parser.parse_args()

    base_url = args.base_url.rstrip("/")
    username = os.environ.get("DEVCORE_TEST_ADMIN_USERNAME", "f09-admin")
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
    _, token, login_status, login_url, _ = submit_login(
        opener,
        base_url,
        username,
        password,
    )
    if (
        token is None
        or login_status != 200
        or "/Admin/Dashboard" not in login_url
        or not has_authentication_cookie(cookies)
    ):
        print("F09 could not establish an authenticated fixture session.", file=sys.stderr)
        return 1

    create_status, _, create_body = request(opener, f"{base_url}/AdminPost/Create")
    form_token = extract_antiforgery_token(create_body)
    if create_status != 200 or form_token is None:
        print("F09 could not load the create form token.", file=sys.stderr)
        return 1

    png_signature = bytes([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])
    fake_jpeg = png_signature + b"f09-fake-extension"
    svg = b"<svg xmlns='http://www.w3.org/2000/svg'><script/></svg>"
    corrupt_png = b"not-a-real-png"
    oversized_png = png_signature + bytes(MAXIMUM_FILE_BYTES + 1 - len(png_signature))

    editor_fake_status, _, editor_fake_body = post_file(
        opener,
        f"{base_url}/AdminPost/UploadEditorImage",
        form_token,
        file_field="file",
        file_name="fake.jpg",
        content_type="image/jpeg",
        content=fake_jpeg,
    )
    editor_svg_status, _, editor_svg_body = post_file(
        opener,
        f"{base_url}/AdminPost/UploadEditorImage",
        form_token,
        file_field="file",
        file_name="vector.svg",
        content_type="image/svg+xml",
        content=svg,
    )
    editor_corrupt_status, _, editor_corrupt_body = post_file(
        opener,
        f"{base_url}/AdminPost/UploadEditorImage",
        form_token,
        file_field="file",
        file_name="corrupt.png",
        content_type="image/png",
        content=corrupt_png,
    )
    editor_oversized_status, _, editor_oversized_body = post_file(
        opener,
        f"{base_url}/AdminPost/UploadEditorImage",
        form_token,
        file_field="file",
        file_name="oversized.png",
        content_type="image/png",
        content=oversized_png,
    )

    legacy_fake_status, _, legacy_fake_body = post_file(
        opener,
        f"{base_url}/AdminPost/UploadImage",
        form_token,
        file_field="file",
        file_name="fake.jpg",
        content_type="image/jpeg",
        content=fake_jpeg,
    )

    create_marker = "F09_CREATE_MUST_NOT_PERSIST"
    create_post_status, create_post_url, create_post_body = post_file(
        opener,
        f"{base_url}/AdminPost/Create",
        form_token,
        file_field="thumbnailFile",
        file_name="vector.svg",
        content_type="image/svg+xml",
        content=svg,
        fields={
            "Title": create_marker,
            "Content": "F09 invalid cover create content",
            "CategoryId": "1001",
            "ThumbnailUrl": "",
            "Summary": "F09 invalid cover create summary",
            "Excerpt": "F09 invalid cover create excerpt",
            "IsPublished": "false",
            "IsActive": "true",
            "PublishDate": "2026-09-22T12:00",
        },
    )
    index_status, _, index_body = request(opener, f"{base_url}/AdminPost")

    edit_status, _, edit_body = request(opener, f"{base_url}/AdminPost/Edit/2001")
    edit_token = extract_antiforgery_token(edit_body)
    edit_marker = "F09_EDIT_MUST_NOT_PERSIST"
    edit_post_status = 0
    edit_post_url = ""
    edit_post_body = ""
    if edit_status == 200 and edit_token is not None:
        edit_post_status, edit_post_url, edit_post_body = post_file(
            opener,
            f"{base_url}/AdminPost/Edit/2001",
            edit_token,
            file_field="thumbnailFile",
            file_name="corrupt.png",
            content_type="image/png",
            content=corrupt_png,
            fields={
                "Id": "2001",
                "Title": edit_marker,
                "Content": "F09 invalid cover edit content",
                "CategoryId": "1001",
                "ThumbnailUrl": "",
                "Summary": "F09 invalid cover edit summary",
                "Excerpt": "F09 invalid cover edit excerpt",
                "IsPublished": "true",
                "IsActive": "true",
                "PublishDate": "2026-09-22T12:00",
            },
        )
    edit_after_status, _, edit_after_body = request(
        opener,
        f"{base_url}/AdminPost/Edit/2001",
    )

    expected_invalid_message = "Invalid image. Upload a valid JPG, PNG, GIF, or WEBP file."
    expected_large_message = "The image exceeds the 8 MB upload limit."
    checks = {
        "editor_rejects_fake_extension": (
            editor_fake_status == 400
            and json_message(editor_fake_body) == expected_invalid_message
        ),
        "editor_rejects_svg": (
            editor_svg_status == 400
            and json_message(editor_svg_body) == expected_invalid_message
        ),
        "editor_rejects_corrupt_image": (
            editor_corrupt_status == 400
            and json_message(editor_corrupt_body) == expected_invalid_message
        ),
        "editor_rejects_oversized_image": (
            editor_oversized_status == 413
            and json_message(editor_oversized_body) == expected_large_message
        ),
        "legacy_endpoint_uses_same_policy_and_json_error": (
            legacy_fake_status == 400
            and json_message(legacy_fake_body) == expected_invalid_message
        ),
        "create_cover_failure_is_visible_and_not_persisted": (
            create_post_status == 200
            and "/AdminPost/Create" in create_post_url
            and expected_invalid_message in create_post_body
            and index_status == 200
            and create_marker not in index_body
        ),
        "edit_cover_failure_is_visible_and_not_persisted": (
            edit_post_status == 200
            and "/AdminPost/Edit/2001" in edit_post_url
            and expected_invalid_message in edit_post_body
            and edit_after_status == 200
            and edit_marker not in edit_after_body
        ),
        "forms_advertise_the_server_allowlist_and_limits": (
            'accept=".jpg,.jpeg,.png,.gif,.webp"' in create_body
            and "max 8 MB" in create_body
            and 'accept=".jpg,.jpeg,.png,.gif,.webp"' in edit_body
            and "max 8 MB" in edit_body
        ),
    }
    statuses = {
        "editor_fake_extension": editor_fake_status,
        "editor_svg": editor_svg_status,
        "editor_corrupt": editor_corrupt_status,
        "editor_oversized": editor_oversized_status,
        "legacy_fake_extension": legacy_fake_status,
        "create_invalid_cover_final": create_post_status,
        "edit_invalid_cover_final": edit_post_status,
    }

    print("F09 isolated image upload HTTP verification")
    print(json.dumps({"checks": checks, "statuses": statuses}, indent=2))
    failed = [name for name, passed in checks.items() if not passed]
    if failed:
        print(f"F09 verification failed: {', '.join(failed)}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
