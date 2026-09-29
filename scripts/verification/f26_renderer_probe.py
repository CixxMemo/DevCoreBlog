#!/usr/bin/env python3
"""Reuse F04 checks and fingerprint the rendered Markdown fixture for F26."""

import argparse
import hashlib

from f01_http_baseline import build_f04_checks, extract_markdown_content
from http_probe_support import cookie_opener, request, wait_until_ready


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-url", required=True)
    args = parser.parse_args()
    base_url = args.base_url.rstrip("/")
    visitor, _ = cookie_opener()
    url = base_url + "/post/f01-markdown-xss"
    wait_until_ready(visitor, url, "F04_SAFE_HEADING", 30)
    status, _, body = request(visitor, url)
    content = extract_markdown_content(body)
    checks = build_f04_checks(body)
    for name, passed in checks.items():
        print(f"f26_markdown_{name}={str(passed).lower()}")
    print("f26_markdown_sha256=" + hashlib.sha256(content.encode()).hexdigest())
    return 0 if status == 200 and content and all(checks.values()) else 1


if __name__ == "__main__":
    raise SystemExit(main())
