#!/usr/bin/env python3
"""Check error response contracts against an app with an unavailable test DB."""

import argparse
import json
import time
import urllib.error
import urllib.request


def fetch(base_url, path, *, data=None, headers=None):
    request = urllib.request.Request(base_url + path, data=data, headers=headers or {})
    try:
        response = urllib.request.urlopen(request, timeout=10)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        return response.status, response.headers, response.read().decode("utf-8", "replace")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-url", required=True)
    parser.add_argument("--baseline", action="store_true")
    parser.add_argument("--fallback", action="store_true")
    args = parser.parse_args()
    for _ in range(120):
        try:
            fetch(args.base_url, "/definitely-missing-f23")
            break
        except (OSError, TimeoutError):
            time.sleep(0.25)
    else:
        raise RuntimeError("Application did not start")

    status_404, headers_404, body_404 = fetch(args.base_url, "/definitely-missing-f23")
    html_status, html_headers, html_body = fetch(args.base_url, "/")
    status_500, headers_500, body_500 = fetch(args.base_url, "/api/categories")
    feed_status, feed_headers, feed_body = fetch(args.base_url, "/api/public/posts/latest")
    invalid_status, invalid_headers, invalid_body = fetch(
        args.base_url, "/api/webhooks/posts", data=b"{invalid",
        headers={"Content-Type": "application/json", "X-DevCore-Secret": "f23-webhook"},
    )
    checks = {
        "unknown_url_is_404_html": status_404 == 404 and "text/html" in headers_404.get("Content-Type", "") and "Page not found" in body_404,
        "db_failure_is_500_html": html_status == 500 and "text/html" in html_headers.get("Content-Type", "") and "Something went wrong" in html_body and "Request ID:" in html_body,
        "db_failure_is_500_json": status_500 == 500 and "application/json" in headers_500.get("Content-Type", "") and json.loads(body_500)["success"] is False,
        "public_feed_failure_is_500_json": feed_status == 500 and "application/json" in feed_headers.get("Content-Type", "") and json.loads(feed_body)["success"] is False,
        "invalid_json_is_400_json": invalid_status == 400 and "json" in invalid_headers.get("Content-Type", ""),
        "no_redirect_or_sensitive_response": all(
            marker not in (body_404 + html_body + body_500 + feed_body + invalid_body)
            for marker in ("f23-isolated-password", "f23-webhook", "Npgsql", "StackTrace", "55499")
        ),
    }
    if args.fallback:
        checks["static_500_fallback_used"] = "<title>Server error</title>" in html_body
    print(json.dumps({"statuses": [status_404, html_status, status_500, feed_status, invalid_status], "checks": checks}, sort_keys=True))
    return 0 if args.baseline or all(checks.values()) else 1


if __name__ == "__main__":
    raise SystemExit(main())
