#!/usr/bin/env python3
"""Check F25 navigation, admin aggregates, and dashboard SQL command count."""

import argparse
import os
import re
import subprocess

from http_probe_support import cookie_opener, request, submit_login


def metric(body: str, label: str, value: int) -> bool:
    pattern = (rf">\s*{re.escape(label)}\s*</(?:div|span)>.*?"
               rf'<div class="text-3xl[^\"]*">\s*{value:,}\s*</div>')
    return re.search(pattern, body, re.DOTALL) is not None


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-url", required=True)
    parser.add_argument("--application-log", required=True)
    parser.add_argument("--pg-port", required=True)
    parser.add_argument("--pg-user", required=True)
    args = parser.parse_args()
    base_url = args.base_url.rstrip("/")

    visitor, _ = cookie_opener()
    home_status, _, home = request(visitor, base_url + "/")
    admin, _ = cookie_opener()
    _, token, login_status, _, _ = submit_login(
        admin, base_url,
        os.environ["DEVCORE_TEST_ADMIN_USERNAME"],
        os.environ["DEVCORE_TEST_ADMIN_PASSWORD"],
    )
    with open(args.application_log, "rb") as app_log:
        app_log.seek(0, 2)
        start = app_log.tell()
    dashboard_status, _, dashboard = request(admin, base_url + "/Admin/Dashboard")
    with open(args.application_log, "rb") as app_log:
        app_log.seek(start)
        dashboard_log = app_log.read().decode("utf-8", "replace")
    automation_status, _, automation = request(admin, base_url + "/Admin/Automations")

    query = (
        'SELECT COUNT(*) FILTER (WHERE "IsActive"), '
        'COUNT(*) FILTER (WHERE "IsActive" AND "IsPublished"), '
        'COUNT(*) FILTER (WHERE "IsActive" AND NOT "IsPublished"), '
        'COALESCE(SUM("ViewCount") FILTER (WHERE "IsActive"), 0) '
        'FROM "Posts";'
        'SELECT COUNT(*) FROM "Categories" WHERE "IsActive";'
    )
    result = subprocess.run(
        ["psql", "-X", "-A", "-t", "-h", "127.0.0.1", "-p", args.pg_port,
         "-U", args.pg_user, "-d", "devcoreblog_f01_test", "-c", query],
        check=True, capture_output=True, text=True,
    )
    totals, category_count = result.stdout.strip().splitlines()
    total_posts, published_posts, draft_posts, total_views = map(int, totals.split("|"))
    category_count = int(category_count)
    sidebar = re.search(r'<aside[^>]*id="main-sidebar".*?<nav[^>]*>(.*?)</nav>',
                        home, re.DOTALL)
    navigation = sidebar.group(1) if sidebar else ""

    sql_count = dashboard_log.count("Executed DbCommand")
    checks = {
        "visitor_home_responds": home_status == 200,
        "navigation_only_active_categories": (
            'href="/kategori/f01-active"' in navigation and
            'href="/kategori/f01-inactive"' not in navigation and
            not any(f'href="/kategori/{slug}"' in navigation for slug in
                    ["haberler", "kesfet", "vibe-coding", "ai", "felsefe"])
        ),
        "home_topics_only_active_categories": (
            "Featured Topics" in home and
            not any(f'href="/kategori/{slug}"' in home for slug in
                    ["f01-inactive", "haberler", "kesfet", "vibe-coding", "ai", "felsefe"])
        ),
        "admin_pages_respond": token is not None and login_status == 200 and
            dashboard_status == 200 and automation_status == 200,
        "dashboard_metrics": all([
            metric(dashboard, "Total Posts", total_posts),
            metric(dashboard, "Published", published_posts),
            metric(dashboard, "Drafts", draft_posts),
            metric(dashboard, "Total Views", total_views),
            metric(dashboard, "Categories", category_count),
        ]),
        "top_post_bounded_and_ranked": (
            "F03 &lt;img" in dashboard and
            dashboard.count("<tr class=\"hover:bg-neutral-50") == 5
        ),
        "automation_active_categories_only": (
            "F01 Active Category" in automation and
            "F01 Inactive Category" not in automation
        ),
        "dashboard_three_sql_commands": sql_count == 3,
    }
    for name, passed in checks.items():
        print(f"f25_{name}={str(passed).lower()}")
    print("f25_navigation_category_links=" + repr(re.findall(
        r'href="(/kategori/[^\"]+)"', navigation)))
    print(f"f25_expected_metrics={total_posts},{published_posts},{draft_posts},"
          f"{total_views},{category_count}")
    print(f"f25_dashboard_sql_command_count={sql_count}")
    return 0 if all(checks.values()) else 1


if __name__ == "__main__":
    raise SystemExit(main())
