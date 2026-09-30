#!/usr/bin/env python3
"""Exercise the preserved webhook contract against an isolated HTTP server."""

import argparse
import json
import subprocess

from http_probe_support import cookie_opener, request, wait_until_ready


def send(opener, base_url, payload=None, secret=None, raw=None, content_type="application/json"):
    headers = {"Content-Type": content_type}
    if secret is not None:
        headers["X-DevCore-Secret"] = secret
    data = raw if raw is not None else json.dumps(payload).encode()
    status, _, body = request(
        opener,
        base_url + "/api/webhooks/posts",
        raw_data=data,
        headers=headers,
    )
    try:
        parsed = json.loads(body)
    except json.JSONDecodeError:
        parsed = {}
    return status, parsed


def query_database(port, user, query):
    result = subprocess.run(
        [
            "psql", "-X", "-h", "127.0.0.1", "-p", str(port),
            "-U", user, "-d", "devcoreblog_f01_test", "-At", "-F", "|",
            "-c", query,
        ],
        check=True,
        capture_output=True,
        text=True,
    )
    return result.stdout.strip()


def field_errors(body):
    return {
        error.get("field")
        for error in body.get("errors", [])
        if isinstance(error, dict)
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-url", required=True)
    parser.add_argument(
        "--mode", choices=("baseline", "disabled", "enabled", "missing"),
        required=True)
    parser.add_argument("--pg-port", type=int, required=True)
    parser.add_argument("--pg-user", required=True)
    args = parser.parse_args()
    base_url = args.base_url.rstrip("/")
    opener, _ = cookie_opener()
    wait_until_ready(opener, base_url + "/Account/Login", "Admin Sign In", 30)
    secret = "f17-webhook"
    valid = {
        "title": "F27 Webhook Contract",
        "content": "F27 isolated webhook content",
        "categoryId": 1001,
    }
    checks = {}
    before_count = int(query_database(
        args.pg_port, args.pg_user, 'SELECT count(*) FROM "Posts";'))

    if args.mode == "baseline":
        unauthorized_malformed, _ = send(
            opener, base_url, secret="invalid-f27-secret", raw=b"{")
        published_status, published_body = send(
            opener, base_url,
            payload={**valid, "title": "F27 Baseline Published", "isPublished": True},
            secret=secret)
        checks = {
            "auth_precedes_json_parsing": unauthorized_malformed == 401,
            "publish_requires_server_permission": (
                published_status == 200
                and published_body.get("isPublished") is False
            ),
        }
    elif args.mode == "missing":
        status, _ = send(
            opener, base_url, payload=valid, secret=secret)
        after_count = int(query_database(
            args.pg_port, args.pg_user, 'SELECT count(*) FROM "Posts";'))
        checks["missing_server_secret_fails_closed"] = (
            status == 401 and after_count == before_count)
    elif args.mode == "enabled":
        status, body = send(
            opener, base_url,
            payload={**valid, "title": "F27 Explicitly Published", "isPublished": True},
            secret=secret)
        post_id = body.get("postId")
        stored = query_database(
            args.pg_port, args.pg_user,
            f'SELECT "IsPublished" FROM "Posts" WHERE "Id" = {post_id};') if isinstance(post_id, int) else ""
        checks["explicit_server_permission_publishes"] = (
            status == 200 and body.get("isPublished") is True and stored == "t")
    else:
        wrong_malformed_status, _ = send(
            opener, base_url, secret="invalid-f27-secret", raw=b"{")
        missing_status, _ = send(opener, base_url, payload=valid)
        wrong_status, _ = send(
            opener, base_url, payload=valid, secret="invalid-f27-secret")
        malformed_status, _ = send(opener, base_url, secret=secret, raw=b"{")
        checks["auth_precedes_json_parsing"] = wrong_malformed_status == 401
        checks["missing_or_wrong_secret_has_generic_401"] = (
            missing_status == 401 and wrong_status == 401)
        checks["authorized_malformed_json_is_400"] = malformed_status == 400

        overpost = {
            **valid,
            "title": "F27 Safe Draft",
            "isPublished": True,
            "slug": "attacker-controlled-slug",
            "createdDate": "2000-01-01T00:00:00Z",
            "viewCount": 999999,
            "isActive": False,
            "id": 999999,
        }
        draft_status, draft_body = send(
            opener, base_url, payload=overpost, secret=secret)
        post_id = draft_body.get("postId")
        checks["success_json_fields_remain_compatible"] = {
            "success", "postId", "title", "slug", "isPublished",
            "publishDate", "message"
        }.issubset(draft_body) and draft_body.get("success") is True
        stored = query_database(
            args.pg_port, args.pg_user,
            f'SELECT "Slug", "IsPublished", "IsActive", "ViewCount", '
            f'"CreatedDate" FROM "Posts" WHERE "Id" = {post_id};') if isinstance(post_id, int) else ""
        parts = stored.split("|")
        checks["publish_disabled_creates_draft"] = (
            draft_status == 200 and draft_body.get("isPublished") is False
            and len(parts) == 5 and parts[1] == "f")
        checks["unknown_system_fields_cannot_overpost"] = (
            len(parts) == 5 and parts[0] != "attacker-controlled-slug"
            and parts[2] == "t" and parts[3] == "0"
            and not parts[4].startswith("2000-")
            and post_id != 999999)

        oversized_status, _ = send(
            opener, base_url, secret=secret, raw=b" " * (2_097_152 + 1))
        wrong_media_status, _ = send(
            opener, base_url, secret=secret, payload=valid,
            content_type="text/plain")
        checks["oversized_body_is_413"] = oversized_status == 413
        checks["wrong_content_type_is_415"] = wrong_media_status == 415

        invalid_cases = (
            ("missing_title", {"content": valid["content"], "categoryId": 1001}, "Title"),
            ("title_too_long", {**valid, "title": "T" * 201}, "Title"),
            ("content_too_long", {**valid, "content": "C" * 200_001}, "Content"),
            ("summary_too_long", {**valid, "summary": "S" * 501}, "Summary"),
            ("excerpt_too_long", {**valid, "excerpt": "E" * 1_001}, "Excerpt"),
            ("cover_url_too_long", {**valid, "coverImageUrl": "https://example.test/" + "x" * 2049}, "CoverImageUrl"),
            ("inactive_category", {**valid, "categoryId": 1002}, "CategoryId"),
        )
        for name, invalid_payload, expected_field in invalid_cases:
            status, body = send(
                opener, base_url, payload=invalid_payload, secret=secret)
            checks[name + "_is_400"] = (
                status == 400 and expected_field in field_errors(body))

        wrong_type_status, _ = send(
            opener, base_url,
            payload={**valid, "content": 7}, secret=secret)
        wrong_date_status, _ = send(
            opener, base_url,
            payload={**valid, "publishDate": "not-a-date"}, secret=secret)
        checks["wrong_json_type_is_400"] = wrong_type_status == 400
        checks["wrong_publish_date_type_is_400"] = wrong_date_status == 400

        after_count = int(query_database(
            args.pg_port, args.pg_user, 'SELECT count(*) FROM "Posts";'))
        checks["rejected_requests_do_not_write"] = after_count == before_count + 1

        rate_statuses = []
        for _ in range(25):
            status, _ = send(
                opener, base_url, payload=valid, secret="invalid-f27-secret")
            rate_statuses.append(status)
            if status == 429:
                break
        checks["webhook_rate_limit_returns_429"] = 429 in rate_statuses

    for name, passed in checks.items():
        print(f"f27_{name}={str(passed).lower()}")
    return 0 if all(checks.values()) else 1


if __name__ == "__main__":
    raise SystemExit(main())
