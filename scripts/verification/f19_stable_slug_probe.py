#!/usr/bin/env python3
"""Verify that edits retain public URLs while new content receives a slug."""

import argparse
import os
import subprocess

from http_probe_support import (
    cookie_opener,
    extract_antiforgery_token,
    request,
    submit_login,
)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-url", required=True)
    parser.add_argument("--pg-port", required=True)
    parser.add_argument("--pg-user", required=True)
    args = parser.parse_args()
    base = args.base_url.rstrip("/")

    def slug(table: str, row_id: int) -> str:
        result = subprocess.run(
            ["psql", "-X", "-h", "127.0.0.1", "-p", args.pg_port,
             "-U", args.pg_user, "-d", "devcoreblog_f01_test",
             "-v", "ON_ERROR_STOP=1", "-A", "-t", "-c",
             f'SELECT "Slug" FROM "{table}" WHERE "Id" = {row_id}'],
            check=True, capture_output=True, text=True,
        )
        return result.stdout.strip()

    visitor, _ = cookie_opener()
    old_post_url = base + "/post/f01-visible"
    old_category_url = base + "/kategori/f01-active"
    post_before = request(visitor, old_post_url)
    category_before = request(visitor, old_category_url)
    original_post_slug = slug("Posts", 2001)
    original_category_slug = slug("Categories", 1001)

    admin, _ = cookie_opener()
    _, login_token, login_status, login_url, _ = submit_login(
        admin, base, os.environ["DEVCORE_TEST_ADMIN_USERNAME"],
        os.environ["DEVCORE_TEST_ADMIN_PASSWORD"],
    )
    post_edit_url = base + "/AdminPost/Edit/2001"
    post_edit_status, _, post_edit_body = request(admin, post_edit_url)
    post_token = extract_antiforgery_token(post_edit_body)
    post_form = {
        "Id": "2001", "Title": "F19 Renamed Post",
        "Content": "F19 stable post address content", "CategoryId": "1001",
        "Summary": "F19_STABLE_POST_MARKER", "Excerpt": "F19 stable excerpt",
        "IsPublished": "true", "IsActive": "true",
        "PublishDate": "2026-09-22T12:00:00",
        "Slug": "attacker-post-slug",
        "__RequestVerificationToken": post_token or "",
    }
    post_save_status, post_save_url, _ = request(
        admin, post_edit_url, data=post_form,
    )
    post_slug_after_edit = slug("Posts", 2001)
    old_post_after_edit = request(visitor, old_post_url)
    new_post_guess = request(visitor, base + "/post/f19-renamed-post")

    # Publication visibility may change, but the stored address must not.
    toggle_statuses = []
    for _ in range(2):
        status, _, _ = request(
            admin, base + "/AdminPost/TogglePublish/2001",
            data={"__RequestVerificationToken": post_token or ""},
        )
        toggle_statuses.append(status)
    old_post_after_toggle = request(visitor, old_post_url)
    post_slug_after_toggle = slug("Posts", 2001)

    post_form["IsActive"] = "false"
    inactive_save_status, _, _ = request(
        admin, post_edit_url, data=post_form,
    )
    inactive_public_status = request(visitor, old_post_url)[0]
    inactive_slug = slug("Posts", 2001)
    post_form["IsActive"] = "true"
    reactivate_save_status, _, _ = request(
        admin, post_edit_url, data=post_form,
    )
    reactivated_public_status = request(visitor, old_post_url)[0]

    category_edit_url = base + "/AdminCategory/Edit/1001"
    category_edit_status, _, category_edit_body = request(admin, category_edit_url)
    category_token = extract_antiforgery_token(category_edit_body)
    category_save_status, category_save_url, _ = request(
        admin, category_edit_url,
        data={"Id": "1001", "Name": "F19 Renamed Category",
              "Slug": "attacker-category-slug",
              "__RequestVerificationToken": category_token or ""},
    )
    category_slug_after_edit = slug("Categories", 1001)
    old_category_after_edit = request(visitor, old_category_url)
    new_category_guess = request(
        visitor, base + "/kategori/f19-renamed-category",
    )

    create_category_status, _, create_category_body = request(
        admin, base + "/AdminCategory/Create",
    )
    create_category_token = extract_antiforgery_token(create_category_body)
    category_create_save_status, _, _ = request(
        admin, base + "/AdminCategory/Create",
        data={"Name": "F19 Fresh Category",
              "__RequestVerificationToken": create_category_token or ""},
    )
    new_category = request(visitor, base + "/kategori/f19-fresh-category")

    create_post_status, _, create_post_body = request(
        admin, base + "/AdminPost/Create",
    )
    create_post_token = extract_antiforgery_token(create_post_body)
    post_create_save_status, _, _ = request(
        admin, base + "/AdminPost/Create",
        data={"Title": "F19 Fresh Post", "Content": "F19 fresh post content",
              "CategoryId": "1001", "Summary": "F19_FRESH_POST_MARKER",
              "Excerpt": "F19 fresh excerpt", "IsPublished": "true",
              "IsActive": "true", "PublishDate": "2026-09-22T12:00:00",
              "__RequestVerificationToken": create_post_token or ""},
    )
    new_post = request(visitor, base + "/post/f19-fresh-post")
    post_edit_after_status, _, post_edit_after_body = request(admin, post_edit_url)
    category_edit_after_status, _, category_edit_after_body = request(
        admin, category_edit_url,
    )

    checks = {
        "admin_session_and_existing_urls": (
            login_token is not None and login_status == 200
            and "/Admin/Dashboard" in login_url
            and post_before[0] == category_before[0] == 200
            and original_post_slug == "f01-visible"
            and original_category_slug == "f01-active"
        ),
        "post_title_edit_keeps_old_url": (
            post_edit_status == post_save_status == 200
            and post_token is not None
            and post_save_url.rstrip("/").endswith("/AdminPost")
            and post_slug_after_edit == original_post_slug
            and old_post_after_edit[0] == 200
            and "F19_STABLE_POST_MARKER" in old_post_after_edit[2]
            and new_post_guess[0] == 404
        ),
        "publish_toggle_keeps_stored_url": (
            toggle_statuses == [200, 200]
            and post_slug_after_toggle == original_post_slug
            and old_post_after_toggle[0] == 200
        ),
        "active_toggle_keeps_stored_url": (
            inactive_save_status == reactivate_save_status == 200
            and inactive_public_status == 404
            and inactive_slug == original_post_slug
            and reactivated_public_status == 200
        ),
        "category_name_edit_keeps_old_url": (
            category_edit_status == category_save_status == 200
            and category_token is not None
            and category_save_url.rstrip("/").endswith("/AdminCategory")
            and category_slug_after_edit == original_category_slug
            and old_category_after_edit[0] == 200
            and "F19 Renamed Category" in old_category_after_edit[2]
            and new_category_guess[0] == 404
        ),
        "new_content_still_gets_slug": (
            create_category_status == category_create_save_status == 200
            and create_post_status == post_create_save_status == 200
            and create_category_token is not None
            and create_post_token is not None
            and new_category[0] == new_post[0] == 200
            and "F19_FRESH_POST_MARKER" in new_post[2]
        ),
        "edit_forms_show_permanent_address": (
            post_edit_after_status == category_edit_after_status == 200
            and "Permanent address" in post_edit_after_body
            and "Permanent address" in category_edit_after_body
            and "f01-visible" in post_edit_after_body
            and "f01-active" in category_edit_after_body
        ),
    }
    for name, passed in checks.items():
        print(f"f19_{name}={str(passed).lower()}")
    print(f"f19_original_post_slug={original_post_slug}")
    print(f"f19_post_slug_after_edit={post_slug_after_edit}")
    print(f"f19_original_category_slug={original_category_slug}")
    print(f"f19_category_slug_after_edit={category_slug_after_edit}")
    return 0 if all(checks.values()) else 1


if __name__ == "__main__":
    raise SystemExit(main())
