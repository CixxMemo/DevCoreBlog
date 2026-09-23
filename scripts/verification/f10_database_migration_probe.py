#!/usr/bin/env python3
"""Verify F10 migration SQL, clean install, and legacy data upgrade."""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import subprocess
import sys


EXPECTED_MIGRATIONS = [
    "20260801205219_InitialCreate",
    "20260806075506_RenamePropertiesForBaseEntity",
    "20260807060210_AddViewCount",
    "20260807084252_AddPublishDate",
    "20260820080943_UpdatePostDataStructure",
    "20260923192150_RestrictCategoryDeletion",
]


def run(
    command: list[str],
    *,
    cwd: Path,
    environment: dict[str, str] | None = None,
) -> str:
    completed = subprocess.run(
        command,
        cwd=cwd,
        env=environment,
        check=False,
        capture_output=True,
        text=True,
    )
    if completed.returncode != 0:
        output = "\n".join(
            line
            for line in (completed.stdout + completed.stderr).splitlines()[-12:]
            if line.strip()
        )
        raise RuntimeError(
            f"Migration verification command failed with exit code "
            f"{completed.returncode}.\n{output}"
        )
    return completed.stdout


def connection_string(host: str, port: int, database: str, user: str) -> str:
    return f"Host={host};Port={port};Database={database};Username={user}"


def ef_environment(connection: str, password_hash: str) -> dict[str, str]:
    environment = os.environ.copy()
    environment.update(
        {
            "ASPNETCORE_ENVIRONMENT": "Development",
            "DOTNET_ENVIRONMENT": "Development",
            "DB_CONNECTION_STRING": connection,
            "ADMIN_USERNAME": "f10-migration-admin",
            "ADMIN_PASSWORD_HASH": password_hash,
            "ADMIN_SESSION_VERSION": "f10",
        }
    )
    environment.pop("ADMIN_PASSWORD", None)
    return environment


def ef_command(
    root: Path,
    connection: str,
    password_hash: str,
    operation: list[str],
) -> str:
    command = ["dotnet", "ef", *operation]
    command.extend(
        [
            "--no-build",
            "--project",
            "DevCoreBlog.csproj",
            "--startup-project",
            "DevCoreBlog.csproj",
            "--context",
            "ApplicationDbContext",
            "--no-color",
        ]
    )
    return run(
        command,
        cwd=root,
        environment=ef_environment(connection, password_hash),
    )


def psql(
    root: Path,
    host: str,
    port: int,
    user: str,
    database: str,
    *,
    sql: str | None = None,
    file: Path | None = None,
) -> str:
    command = [
        "psql",
        "-X",
        "-h",
        host,
        "-p",
        str(port),
        "-U",
        user,
        "-d",
        database,
        "-v",
        "ON_ERROR_STOP=1",
        "-A",
        "-t",
    ]
    if sql is not None:
        command.extend(["-c", sql])
    elif file is not None:
        command.extend(["-f", str(file)])
    else:
        raise ValueError("sql or file is required")
    return run(command, cwd=root).strip()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path.cwd())
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--user", required=True)
    parser.add_argument("--empty-database", required=True)
    parser.add_argument("--legacy-database", required=True)
    args = parser.parse_args()

    password_hash = os.environ.get("DEVCORE_TEST_ADMIN_PASSWORD_HASH", "")
    if not password_hash:
        print("DEVCORE_TEST_ADMIN_PASSWORD_HASH is required.", file=sys.stderr)
        return 2

    root = args.root.resolve()
    sql_path = root / ".f10-empty-schema.sql"
    f15_delta_path = root / ".f15-category-delete-rule.sql"
    empty_connection = connection_string(
        args.host,
        args.port,
        args.empty_database,
        args.user,
    )
    legacy_connection = connection_string(
        args.host,
        args.port,
        args.legacy_database,
        args.user,
    )

    try:
        ef_command(
            root,
            empty_connection,
            password_hash,
            [
                "migrations",
                "script",
                "--idempotent",
                "--output",
                str(sql_path),
            ],
        )
        generated_sql = sql_path.read_text(encoding="utf-8")
        ef_command(
            root,
            empty_connection,
            password_hash,
            [
                "migrations",
                "script",
                EXPECTED_MIGRATIONS[-2],
                EXPECTED_MIGRATIONS[-1],
                "--output",
                str(f15_delta_path),
            ],
        )
        f15_delta_sql = f15_delta_path.read_text(encoding="utf-8")
        normalized_f15_delta_sql = f15_delta_sql.upper()
        psql(
            root,
            args.host,
            args.port,
            args.user,
            args.empty_database,
            file=sql_path,
        )

        empty_report = json.loads(
            psql(
                root,
                args.host,
                args.port,
                args.user,
                args.empty_database,
                sql="""
                    SELECT json_build_object(
                        'migration_count', (
                            SELECT COUNT(*) FROM "__EFMigrationsHistory"
                        ),
                        'category_table_exists', to_regclass('"Categories"') IS NOT NULL,
                        'post_table_exists', to_regclass('"Posts"') IS NOT NULL,
                        'post_column_count', (
                            SELECT COUNT(*)
                            FROM information_schema.columns
                            WHERE table_schema = 'public'
                              AND table_name = 'Posts'
                        ),
                        'category_delete_rule', (
                            SELECT delete_rule
                            FROM information_schema.referential_constraints
                            WHERE constraint_schema = 'public'
                              AND constraint_name = 'FK_Posts_Categories_CategoryId'
                        ),
                        'post_count', (SELECT COUNT(*) FROM "Posts")
                    );
                """,
            )
        )

        ef_command(
            root,
            legacy_connection,
            password_hash,
            [
                "database",
                "update",
                EXPECTED_MIGRATIONS[0],
                "--connection",
                legacy_connection,
            ],
        )
        psql(
            root,
            args.host,
            args.port,
            args.user,
            args.legacy_database,
            sql="""
                INSERT INTO "Categories" ("Id", "Name", "Slug")
                VALUES (9100, 'F10 Legacy Category', 'f10-legacy');

                INSERT INTO "Posts" (
                    "Id", "Title", "Slug", "Summary", "Content",
                    "CreatedAt", "IsPublished", "CategoryId"
                ) VALUES
                    (9101, 'F10 Legacy Published', 'f10-legacy-published',
                     'published summary', 'published content',
                     '2026-01-01 00:00:00+00', true, 9100),
                    (9102, 'F10 Legacy Draft', 'f10-legacy-draft',
                     'draft summary', 'draft content',
                     '2026-01-02 00:00:00+00', false, 9100);
            """,
        )
        ef_command(
            root,
            legacy_connection,
            password_hash,
            ["database", "update", "--connection", legacy_connection],
        )

        legacy_history_count = int(
            psql(
                root,
                args.host,
                args.port,
                args.user,
                args.legacy_database,
                sql='SELECT COUNT(*) FROM "__EFMigrationsHistory";',
            )
        )
        legacy_rows_output = psql(
            root,
            args.host,
            args.port,
            args.user,
            args.legacy_database,
            sql="""
                SELECT "Title", "IsActive", "IsPublished", "ViewCount",
                       "Excerpt", "ThumbnailUrl"
                FROM "Posts"
                ORDER BY "Id";
            """,
        )
        legacy_rows = [line.split("|") for line in legacy_rows_output.splitlines()]
        legacy_delete_rule = psql(
            root,
            args.host,
            args.port,
            args.user,
            args.legacy_database,
            sql="""
                SELECT delete_rule
                FROM information_schema.referential_constraints
                WHERE constraint_schema = 'public'
                  AND constraint_name = 'FK_Posts_Categories_CategoryId';
            """,
        )

        checks = {
            "sql_contains_all_migrations": all(
                migration in generated_sql for migration in EXPECTED_MIGRATIONS
            ),
            "forward_sql_contains_no_drop_table": (
                "DROP TABLE" not in generated_sql.upper()
            ),
            "empty_database_reaches_latest_schema": (
                empty_report["migration_count"] == len(EXPECTED_MIGRATIONS)
                and empty_report["category_table_exists"] is True
                and empty_report["post_table_exists"] is True
                and empty_report["post_column_count"] == 13
                and empty_report["category_delete_rule"] == "RESTRICT"
                and empty_report["post_count"] == 0
            ),
            "legacy_database_applies_all_migrations": (
                legacy_history_count == len(EXPECTED_MIGRATIONS)
                and legacy_delete_rule == "RESTRICT"
            ),
            "f15_delta_changes_only_the_category_foreign_key": (
                normalized_f15_delta_sql.count('ALTER TABLE "POSTS"') == 2
                and 'DROP CONSTRAINT "FK_POSTS_CATEGORIES_CATEGORYID"'
                    in normalized_f15_delta_sql
                and 'ADD CONSTRAINT "FK_POSTS_CATEGORIES_CATEGORYID"'
                    in normalized_f15_delta_sql
                and "ON DELETE RESTRICT" in normalized_f15_delta_sql
                and all(
                    forbidden not in normalized_f15_delta_sql
                    for forbidden in (
                        "DROP TABLE",
                        "DROP COLUMN",
                        "ALTER COLUMN",
                        "CREATE TABLE",
                        "UPDATE \"",
                    )
                )
            ),
            "legacy_rows_are_preserved_without_auto_publish": legacy_rows == [
                ["F10 Legacy Published", "t", "f", "0", "", ""],
                ["F10 Legacy Draft", "f", "f", "0", "", ""],
            ],
        }
        report = {
            "checks": checks,
            "empty_database": empty_report,
            "legacy_database": {
                "migration_count": legacy_history_count,
                "category_delete_rule": legacy_delete_rule,
                "rows": [
                    {
                        "title": row[0],
                        "is_active": row[1] == "t",
                        "is_published": row[2] == "t",
                        "view_count": int(row[3]),
                        "excerpt_is_empty": row[4] == "",
                        "thumbnail_is_empty": row[5] == "",
                    }
                    for row in legacy_rows
                ],
            },
        }
        print("F10 isolated migration verification")
        print(json.dumps(report, indent=2))
        failed = [name for name, passed in checks.items() if not passed]
        if failed:
            print(f"F10 verification failed: {', '.join(failed)}", file=sys.stderr)
            return 1
        return 0
    except (RuntimeError, ValueError, json.JSONDecodeError) as error:
        print(str(error), file=sys.stderr)
        return 1
    finally:
        sql_path.unlink(missing_ok=True)
        f15_delta_path.unlink(missing_ok=True)


if __name__ == "__main__":
    raise SystemExit(main())
