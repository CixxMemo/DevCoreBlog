#!/bin/sh
set -eu
task_repo=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
task_report=$(mktemp -d /tmp/devcore-f42-current-dates.XXXXXX)
trap 'rm -rf -- "$task_report"' EXIT INT TERM
sh "$task_repo/scripts/verification/run_site_dates_probe.sh" "$task_report"
