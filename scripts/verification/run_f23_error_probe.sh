#!/bin/sh
set -eu

task_repo=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
task_tmp=$(mktemp -d /tmp/devcoreblog-f23.XXXXXX)
task_source="$task_tmp/source"
task_port=${DEVCORE_F23_APP_PORT:-15167}
task_pid=
python3 - "$task_port" <<'PY'
import socket
import sys
with socket.socket() as probe:
    if probe.connect_ex(("127.0.0.1", int(sys.argv[1]))) == 0:
        raise SystemExit("F23 test port is occupied")
PY
cleanup() {
    if [ -n "$task_pid" ]; then
        kill "$task_pid" 2>/dev/null || true
        wait "$task_pid" 2>/dev/null || true
    fi
    case "$task_tmp" in /tmp/devcoreblog-f23.*) rm -rf -- "$task_tmp" ;; esac
}
trap cleanup EXIT INT TERM

mkdir -p "$task_source"
mkdir -m 700 "$task_tmp/keys"
rsync -a --exclude .git --exclude '.env*' --exclude bin --exclude obj \
    "$task_repo/" "$task_source/"
for task_project in . DevCoreBlog.Core DevCoreBlog.Data DevCoreBlog.Services tools/DevCoreBlog.PasswordHashTool; do
    if [ -d "$task_repo/$task_project/obj" ]; then
        mkdir -p "$task_source/$task_project/obj"
        rsync -a "$task_repo/$task_project/obj/" "$task_source/$task_project/obj/"
    fi
done
: > "$task_source/.env"
if [ "${DEVCORE_F23_BREAK_ERROR_VIEW:-0}" = 1 ]; then
    rm "$task_source/Views/Shared/Error.cshtml"
    task_fallback_flag=--fallback
else
    task_fallback_flag=
fi
(
    cd "$task_source"
    dotnet build DevCoreBlog.csproj --no-restore --nologo -t:Rebuild \
        --disable-build-servers -m:1 -p:NuGetAudit=false -nodeReuse:false >/dev/null
    dotnet build tools/DevCoreBlog.PasswordHashTool/DevCoreBlog.PasswordHashTool.csproj \
        --no-restore --nologo --disable-build-servers -m:1 \
        -p:NuGetAudit=false -nodeReuse:false >/dev/null
)
task_hash=$(printf '%s\n' 'f23-isolated-password' | (
    cd "$task_source"
    dotnet run --no-build --project tools/DevCoreBlog.PasswordHashTool/DevCoreBlog.PasswordHashTool.csproj -- --stdin
))
(
    cd "$task_source"
    exec env -u ADMIN_PASSWORD ASPNETCORE_ENVIRONMENT=Production DOTNET_ENVIRONMENT=Production \
        ASPNETCORE_URLS="http://127.0.0.1:$task_port" \
        DATA_PROTECTION_KEYS_PATH="$task_tmp/keys" \
        DB_CONNECTION_STRING='Host=127.0.0.1;Port=55499;Database=f23_unavailable;Username=f23;Timeout=1' \
        ADMIN_USERNAME=f23-admin ADMIN_PASSWORD_HASH="$task_hash" \
        CLOUDINARY_CLOUD_NAME=f23-cloud CLOUDINARY_API_KEY=f23-key \
        CLOUDINARY_API_SECRET=f23-secret WEBHOOK_API_SECRET=f23-webhook \
        dotnet bin/Debug/net10.0/DevCoreBlog.dll > "$task_tmp/application.log" 2>&1
) &
task_pid=$!
python3 "$task_source/scripts/verification/f23_error_probe.py" \
    --base-url "http://127.0.0.1:$task_port" $task_fallback_flag "$@"
if [ "${DEVCORE_F23_HOLD_FOR_BROWSER:-0}" = 1 ]; then
    printf 'F23 browser fixture ready at http://127.0.0.1:%s\n' "$task_port"
    while kill -0 "$task_pid" 2>/dev/null; do sleep 1; done
fi
