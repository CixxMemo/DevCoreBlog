#!/usr/bin/env python3
"""Check the synthetic publication matrix across visitor and admin HTTP routes."""

import argparse
import json
import os
from urllib.parse import urlencode

from http_probe_support import (
    cookie_opener,
    has_authentication_cookie,
    request,
    request_with_headers,
    submit_login,
    wait_until_ready,
)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-url", required=True)
    args = parser.parse_args()
    base_url = args.base_url.rstrip("/")
    visitor, _ = cookie_opener()
    wait_until_ready(visitor, f"{base_url}/post/f01-visible", "F01_VISIBLE_MARKER", 30)

    public_paths = [
        "/", "/kategori/f01-active", "/ara?query=F01_", "/post/f01-visible",
        "/sitemap.xml", "/api/public/posts/latest",
    ]
    hidden = ["F01_FUTURE_VISIBLE_MARKER", "F01_DRAFT_MARKER",
              "F01_INACTIVE_MARKER", "F01_DUPLICATE_TITLE_MARKER"]
    responses = {path: request_with_headers(visitor, base_url + path)
                 for path in public_paths}
    feed_status, _, feed_body, feed_headers = responses["/api/public/posts/latest"]
    feed = json.loads(feed_body) if feed_status == 200 else []

    detail_statuses = {
        slug: request(visitor, f"{base_url}/post/{slug}")[0]
        for slug in ["f01-future-visible-marker", "f01-draft",
                     "f01-inactive-post", "f01-duplicate-title"]
    }
    inactive_category_status = request(visitor, f"{base_url}/kategori/f01-inactive")[0]
    sitemap_body = responses["/sitemap.xml"][2]
    api_categories_status, _, api_categories_body = request(visitor, f"{base_url}/api/categories")
    api_categories = json.loads(api_categories_body) if api_categories_status == 200 else []

    admin, cookies = cookie_opener()
    login_get, token, login_status, login_url, _ = submit_login(
        admin, base_url,
        os.environ["DEVCORE_TEST_ADMIN_USERNAME"],
        os.environ["DEVCORE_TEST_ADMIN_PASSWORD"],
    )
    admin_status, _, _ = request(admin, f"{base_url}/AdminPost")
    # F41 pages the inventory; locate each publication case instead of expecting
    # every legacy row on page one when the performance fixture has grown.
    admin_markers = ["F01 Future Post", "F01 Draft Post", "F01 Inactive Post", "F01 Visible Post"]
    admin_cases = [request(admin, base_url + '/AdminPost?' + urlencode({'query': marker}))
                   for marker in admin_markers]

    checks = {
        "public_routes_respond": all(result[0] == 200 for result in responses.values()),
        "public_lists_hide_all_four_cases": all(
            marker not in response[2]
            for response in responses.values() for marker in hidden
        ),
        "public_navigation_hides_inactive_category": all(
            "F01 Inactive Category" not in response[2]
            for path, response in responses.items()
            if path != "/api/public/posts/latest"
        ),
        "details_return_404": all(status == 404 for status in detail_statuses.values()),
        "inactive_category_returns_404": inactive_category_status == 404,
        "sitemap_hides_inactive_category": "f01-inactive" not in sitemap_body,
        "api_categories_hide_inactive": api_categories_status == 200 and
            all(category["slug"] != "f01-inactive" for category in api_categories),
        "feed_preserves_shape_and_limit": len(feed) <= 3 and all(
            {"id", "title", "slug", "summary", "excerpt", "coverImageUrl",
             "publishDate", "url", "categoryName"} <= set(post)
            for post in feed
        ),
        "feed_is_not_stored": "no-store" in feed_headers.get("Cache-Control", "").lower(),
        "public_html_is_not_stored": all(
            "no-store" in response[3].get("Cache-Control", "").lower()
            for path, response in responses.items()
            if path != "/api/public/posts/latest"
        ),
        "admin_keeps_all_rows": login_get == 200 and token is not None and
            login_status == 200 and "/Admin/Dashboard" in login_url and
            has_authentication_cookie(cookies) and admin_status == 200 and
            all(case[0] == 200 and marker in case[2]
                for marker, case in zip(admin_markers, admin_cases)),
    }
    for name, passed in checks.items():
        print(f"f17_http_{name}={str(passed).lower()}")
    print(f"f17_http_detail_statuses={json.dumps(detail_statuses, sort_keys=True)}")
    return 0 if all(checks.values()) else 1


if __name__ == "__main__":
    raise SystemExit(main())
