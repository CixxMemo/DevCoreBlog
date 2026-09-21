#!/usr/bin/env python3
"""Bound EF migration discovery so a hanging design-time host is visible."""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import signal
import subprocess


def probe(root: Path, project: str, timeout_seconds: int) -> dict[str, object]:
    command = [
        "dotnet", "ef", "migrations", "list", "--no-connect", "--no-build",
        "--project", project, "--startup-project", "DevCoreBlog.csproj",
        "--context", "ApplicationDbContext", "--no-color", "--verbose",
    ]
    environment = os.environ.copy()
    environment["DB_CONNECTION_STRING"] = (
        "Host=127.0.0.1;Port=55432;Database=devcoreblog_f01_test;Username=f01"
    )
    environment["ADMIN_USERNAME"] = "f01-migration-admin"
    environment["ADMIN_PASSWORD"] = "f01-migration-password"
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

    lines = [line for line in output.splitlines() if line.strip()]
    return {
        "project": project,
        "status": "timed_out" if timed_out else "completed",
        "exit_code": None if timed_out else process.returncode,
        "tail": lines[-8:],
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path.cwd())
    parser.add_argument("--timeout-seconds", type=int, default=12)
    args = parser.parse_args()

    results = [
        probe(args.root, "DevCoreBlog.Data/DevCoreBlog.Data.csproj", args.timeout_seconds),
        probe(args.root, "DevCoreBlog.csproj", args.timeout_seconds),
    ]
    print(json.dumps({"migration_discovery": results}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
