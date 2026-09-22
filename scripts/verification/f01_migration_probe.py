#!/usr/bin/env python3
"""Verify bounded EF migration discovery for the Web migration assembly."""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import signal
import subprocess


EXPECTED_MIGRATIONS = [
    "20260801205219_InitialCreate",
    "20260806075506_RenamePropertiesForBaseEntity",
    "20260807060210_AddViewCount",
    "20260807084252_AddPublishDate",
    "20260820080943_UpdatePostDataStructure",
]


def probe(root: Path, timeout_seconds: int) -> tuple[dict[str, object], bool]:
    command = [
        "dotnet", "ef", "migrations", "list", "--no-connect", "--no-build",
        "--project", "DevCoreBlog.csproj",
        "--startup-project", "DevCoreBlog.csproj",
        "--context", "ApplicationDbContext", "--no-color", "--verbose",
    ]
    environment = os.environ.copy()
    environment["DB_CONNECTION_STRING"] = (
        "Host=127.0.0.1;Port=55432;Database=devcoreblog_f01_test;Username=f01"
    )
    environment["ADMIN_USERNAME"] = "f01-migration-admin"
    environment.pop("ADMIN_PASSWORD", None)
    environment["ADMIN_PASSWORD_HASH"] = os.environ[
        "DEVCORE_TEST_ADMIN_PASSWORD_HASH"
    ]
    environment["ASPNETCORE_ENVIRONMENT"] = "Development"
    environment["DOTNET_ENVIRONMENT"] = "Development"
    process = subprocess.Popen(
        command,
        cwd=root,
        env=environment,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        start_new_session=True,
    )
    timed_out = False
    try:
        output, _ = process.communicate(timeout=timeout_seconds)
    except subprocess.TimeoutExpired:
        timed_out = True
        os.killpg(process.pid, signal.SIGTERM)
        try:
            output, _ = process.communicate(timeout=3)
        except subprocess.TimeoutExpired:
            os.killpg(process.pid, signal.SIGKILL)
            output, _ = process.communicate()

    migration_positions = {
        migration: output.find(migration) for migration in EXPECTED_MIGRATIONS
    }
    discovered = [
        migration
        for migration, position in sorted(
            migration_positions.items(),
            key=lambda item: item[1],
        )
        if position >= 0
    ]
    version_mismatch = (
        "tools version" in output.lower()
        and "older than that of the runtime" in output.lower()
    )
    checks = {
        "command_completed": not timed_out and process.returncode == 0,
        "five_historical_migrations_discovered_in_order": (
            discovered == EXPECTED_MIGRATIONS
        ),
        "tool_runtime_patch_warning_absent": not version_mismatch,
        "no_missing_migrations_message": "No migrations were found." not in output,
    }
    result = {
        "project": "DevCoreBlog.csproj",
        "status": "timed_out" if timed_out else "completed",
        "exit_code": None if timed_out else process.returncode,
        "migrations": discovered,
        "checks": checks,
    }
    return result, all(checks.values())


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path.cwd())
    parser.add_argument("--timeout-seconds", type=int, default=12)
    args = parser.parse_args()

    result, passed = probe(args.root, args.timeout_seconds)
    print(json.dumps({"migration_discovery": result}, indent=2))
    return 0 if passed else 1


if __name__ == "__main__":
    raise SystemExit(main())
