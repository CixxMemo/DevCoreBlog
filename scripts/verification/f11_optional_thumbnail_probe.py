#!/usr/bin/env python3
"""Verify optional cover handling and visible post form validation for F11."""

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


EXISTING_POST_ID = 2003
EXISTING_COVER = "https://images.example.test/f11-existing-cover.png"


def post_fields(
    title: str,
    content: str,
    *,
    post_id: int | None = None,
) -> dict[str, str]:
    fields = {
        "Title": title,
        "Content": content,
        "CategoryId": "1001",
        "Summary": f"{title} summary",
        "Excerpt": f"{title} excerpt",
        "IsPublished": "false",
        "IsActive": "true",
        "PublishDate": "2026-09-22T12:00",
    }
    if post_id is not None:
        fields["Id"] = str(post_id)
    return fields


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-url", default="http://127.0.0.1:15159")
    parser.add_argument("--wait-seconds", type=int, default=30)
    args = parser.parse_args()

    base_url = args.base_url.rstrip("/")
    username = os.environ.get("DEVCORE_TEST_ADMIN_USERNAME", "f11-admin")
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
        print("F11 could not establish an authenticated fixture session.", file=sys.stderr)
        return 1

    create_status, _, create_body = request(opener, f"{base_url}/AdminPost/Create")
    create_token = extract_antiforgery_token(create_body)
    if create_status != 200 or create_token is None:
        print("F11 could not load the create form token.", file=sys.stderr)
        return 1

    create_marker = "F11_CREATE_WITHOUT_COVER"
    create_fields = post_fields(create_marker, "F11 coverless create content")
    create_fields["__RequestVerificationToken"] = create_token
    create_save_status, create_save_url, create_save_body = request(
        opener,
        f"{base_url}/AdminPost/Create",
        data=create_fields,
    )

    edit_status, _, edit_body = request(
        opener,
        f"{base_url}/AdminPost/Edit/{EXISTING_POST_ID}",
    )
    edit_token = extract_antiforgery_token(edit_body)
    if edit_status != 200 or edit_token is None:
        print("F11 could not load the edit form token.", file=sys.stderr)
        return 1

    edit_marker = "F11_EDIT_WITHOUT_NEW_COVER"
    malicious_cover = "https://attacker.invalid/client-cover.png"
    edit_fields = post_fields(
        edit_marker,
        "F11 edit content without replacement cover",
        post_id=EXISTING_POST_ID,
    )
    edit_fields["ThumbnailUrl"] = malicious_cover
    edit_fields["__RequestVerificationToken"] = edit_token
    edit_save_status, edit_save_url, _ = request(
        opener,
        f"{base_url}/AdminPost/Edit/{EXISTING_POST_ID}",
        data=edit_fields,
    )
    edit_after_status, _, edit_after_body = request(
        opener,
        f"{base_url}/AdminPost/Edit/{EXISTING_POST_ID}",
    )

    invalid_marker = "F11_VALIDATION_INPUT_RETAINED"
    invalid_fields = post_fields(
        invalid_marker,
        "F11 validation content retained",
    )
    invalid_fields["CategoryId"] = ""
    invalid_fields["__RequestVerificationToken"] = create_token
    invalid_status, invalid_url, invalid_body = request(
        opener,
        f"{base_url}/AdminPost/Create",
        data=invalid_fields,
    )
    index_after_invalid_status, _, index_after_invalid_body = request(
        opener,
        f"{base_url}/AdminPost",
    )

    failed_upload_marker = "F11_FAILED_UPLOAD_INPUT_RETAINED"
    failed_upload_fields = post_fields(
        failed_upload_marker,
        "F11 failed upload content retained",
        post_id=EXISTING_POST_ID,
    )
    corrupt_png = b"not-a-real-png"
    failed_upload_status, failed_upload_url, failed_upload_body = post_file(
        opener,
        f"{base_url}/AdminPost/Edit/{EXISTING_POST_ID}",
        extract_antiforgery_token(edit_after_body) or "",
        file_field="thumbnailFile",
        file_name="corrupt.png",
        content_type="image/png",
        content=corrupt_png,
        fields=failed_upload_fields,
    )
    edit_after_failure_status, _, edit_after_failure_body = request(
        opener,
        f"{base_url}/AdminPost/Edit/{EXISTING_POST_ID}",
    )

    invalid_image_message = "Invalid image. Upload a valid JPG, PNG, GIF, or WEBP file."
    checks = {
        "create_form_has_optional_cover_and_hides_empty_summary": (
            'name="thumbnailFile"' in create_body
            and 'name="ThumbnailUrl"' not in create_body
            and 'data-valmsg-summary="true"' not in create_body
        ),
        "create_without_cover_persists": (
            create_save_status == 200
            and create_save_url.rstrip("/").endswith("/AdminPost")
            and create_marker in create_save_body
        ),
        "edit_form_does_not_post_stored_cover": (
            EXISTING_COVER in edit_body
            and 'name="ThumbnailUrl"' not in edit_body
            and 'data-valmsg-summary="true"' not in edit_body
        ),
        "edit_without_file_preserves_server_cover": (
            edit_save_status == 200
            and edit_save_url.rstrip("/").endswith("/AdminPost")
            and edit_after_status == 200
            and edit_marker in edit_after_body
            and EXISTING_COVER in edit_after_body
            and malicious_cover not in edit_after_body
        ),
        "validation_failure_retains_inputs_and_does_not_persist": (
            invalid_status == 200
            and "/AdminPost/Create" in invalid_url
            and invalid_marker in invalid_body
            and "F11 validation content retained" in invalid_body
            and 'data-valmsg-summary="true"' in invalid_body
            and "validation-summary-errors" in invalid_body
            and index_after_invalid_status == 200
            and invalid_marker not in index_after_invalid_body
        ),
        "upload_failure_retains_inputs_cover_and_database_state": (
            failed_upload_status == 200
            and f"/AdminPost/Edit/{EXISTING_POST_ID}" in failed_upload_url
            and invalid_image_message in failed_upload_body
            and failed_upload_marker in failed_upload_body
            and "F11 failed upload content retained" in failed_upload_body
            and EXISTING_COVER in failed_upload_body
            and edit_after_failure_status == 200
            and edit_marker in edit_after_failure_body
            and failed_upload_marker not in edit_after_failure_body
            and EXISTING_COVER in edit_after_failure_body
        ),
    }
    statuses = {
        "create_without_cover_final": create_save_status,
        "edit_without_file_final": edit_save_status,
        "validation_failure_final": invalid_status,
        "failed_upload_final": failed_upload_status,
    }

    print("F11 isolated optional cover and validation verification")
    print(json.dumps({"checks": checks, "statuses": statuses}, indent=2))
    failed = [name for name, passed in checks.items() if not passed]
    if failed:
        print(f"F11 verification failed: {', '.join(failed)}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
