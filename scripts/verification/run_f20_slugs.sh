#!/bin/sh
set -eu

# Exercise the real migration chain and HTTP creates in disposable databases.
task_repo=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
task_tmp=$(mktemp -d /tmp/devcoreblog-f20.XXXXXX)
task_source="$task_tmp/source"
task_pgdata="$task_tmp/postgres"
task_socket="$task_tmp/socket"
task_pg_port=${DEVCORE_F20_PG_PORT:-55436}
task_app_port=${DEVCORE_F20_APP_PORT:-15162}
task_pg_user=$(id -un)
task_admin_password=f20-isolated-password
task_app_pid=
task_pg_started=0

cleanup() {
    if [ -n "$task_app_pid" ]; then
        kill "$task_app_pid" 2>/dev/null || true
        wait "$task_app_pid" 2>/dev/null || true
    fi
    if [ "$task_pg_started" -eq 1 ]; then
        pg_ctl -D "$task_pgdata" -m fast -w stop >/dev/null 2>&1 || true
    fi
    case "$task_tmp" in
        /tmp/devcoreblog-f20.*) rm -rf -- "$task_tmp" ;;
        *) printf 'Unexpected temporary path: %s\n' "$task_tmp" >&2 ;;
    esac
}
trap cleanup EXIT INT TERM

for task_command in dotnet rsync initdb pg_ctl createdb psql python3; do
    command -v "$task_command" >/dev/null 2>&1 || {
        printf 'Missing required command: %s\n' "$task_command" >&2
        exit 1
    }
done
pg_isready -h 127.0.0.1 -p "$task_pg_port" >/dev/null 2>&1 && {
    printf 'Test PostgreSQL port %s is occupied.\n' "$task_pg_port" >&2
    exit 1
}

mkdir -p "$task_source" "$task_socket"
rsync -a --exclude .git --exclude '.env*' --exclude bin --exclude obj \
    --exclude cookies.txt --exclude .DS_Store "$task_repo/" "$task_source/"
for task_project in . DevCoreBlog.Core DevCoreBlog.Data DevCoreBlog.Services \
    tools/DevCoreBlog.PasswordHashTool; do
    if [ -d "$task_repo/$task_project/obj" ]; then
        mkdir -p "$task_source/$task_project/obj"
        rsync -a "$task_repo/$task_project/obj/" "$task_source/$task_project/obj/"
    fi
done
: > "$task_source/.env"

(
    cd "$task_source"
    dotnet build DevCoreBlog.csproj --no-restore --nologo \
        --disable-build-servers -m:1 -p:NuGetAudit=false -nodeReuse:false
    dotnet build tools/DevCoreBlog.PasswordHashTool/DevCoreBlog.PasswordHashTool.csproj \
        --no-restore --nologo --disable-build-servers -m:1 \
        -p:NuGetAudit=false -nodeReuse:false
)

task_admin_password_hash=$(
    printf '%s\n' "$task_admin_password" | (
        cd "$task_source"
        dotnet run --no-build \
            --project tools/DevCoreBlog.PasswordHashTool/DevCoreBlog.PasswordHashTool.csproj \
            -- --stdin
    )
)
initdb -D "$task_pgdata" --no-locale --encoding=UTF8 --auth=trust >/dev/null
pg_ctl -D "$task_pgdata" -l "$task_tmp/postgres.log" \
    -o "-p $task_pg_port -h 127.0.0.1 -k $task_socket" -w start >/dev/null
task_pg_started=1
createdb -h 127.0.0.1 -p "$task_pg_port" -U "$task_pg_user" f20_empty
createdb -h 127.0.0.1 -p "$task_pg_port" -U "$task_pg_user" f20_prior

task_empty_connection="Host=127.0.0.1;Port=$task_pg_port;Database=f20_empty;Username=$task_pg_user"
task_prior_connection="Host=127.0.0.1;Port=$task_pg_port;Database=f20_prior;Username=$task_pg_user"
(
    cd "$task_source"
    DB_CONNECTION_STRING="$task_empty_connection" dotnet ef database update \
        --no-build --project DevCoreBlog.csproj --startup-project DevCoreBlog.csproj \
        --context ApplicationDbContext --connection "$task_empty_connection" --no-color
    DB_CONNECTION_STRING="$task_prior_connection" dotnet ef database update \
        20260923192150_RestrictCategoryDeletion --no-build \
        --project DevCoreBlog.csproj --startup-project DevCoreBlog.csproj \
        --context ApplicationDbContext --connection "$task_prior_connection" --no-color
)

psql -X -h 127.0.0.1 -p "$task_pg_port" -U "$task_pg_user" -d f20_prior \
    -v ON_ERROR_STOP=1 -f "$task_source/scripts/verification/f20_slug_seed.sql" >/dev/null
printf 'F20 prior-schema read-only collision inventory:\n'
psql -X -h 127.0.0.1 -p "$task_pg_port" -U "$task_pg_user" -d f20_prior \
    -v ON_ERROR_STOP=1 -f "$task_source/scripts/verification/f20_slug_inventory.sql"
(
    cd "$task_source"
    DB_CONNECTION_STRING="$task_prior_connection" dotnet ef database update \
        --no-build --project DevCoreBlog.csproj --startup-project DevCoreBlog.csproj \
        --context ApplicationDbContext --connection "$task_prior_connection" --no-color
)

(
    cd "$task_source"
    exec env -u ADMIN_PASSWORD \
        TZ=UTC SITE_TIME_ZONE=Europe/Istanbul \
        ASPNETCORE_ENVIRONMENT=Development DOTNET_ENVIRONMENT=Development \
        DB_CONNECTION_STRING="$task_prior_connection" \
        ADMIN_USERNAME=f20-admin ADMIN_PASSWORD_HASH="$task_admin_password_hash" \
        CLOUDINARY_CLOUD_NAME=f20-cloud CLOUDINARY_API_KEY=f20-key \
        CLOUDINARY_API_SECRET=f20-secret WEBHOOK_API_SECRET=f20-webhook \
        dotnet run --no-build --project DevCoreBlog.csproj \
        --urls "http://127.0.0.1:$task_app_port" > "$task_tmp/application.log" 2>&1
) &
task_app_pid=$!

DEVCORE_TEST_ADMIN_USERNAME=f20-admin \
DEVCORE_TEST_ADMIN_PASSWORD="$task_admin_password" \
python3 "$task_source/scripts/verification/f20_slug_probe.py" \
    --base-url "http://127.0.0.1:$task_app_port" \
    --pg-port "$task_pg_port" --pg-user "$task_pg_user"

if [ "${DEVCORE_F20_HOLD_FOR_BROWSER:-0}" = 1 ]; then
    printf 'F20 browser fixture ready at http://127.0.0.1:%s\n' "$task_app_port"
    while kill -0 "$task_app_pid" 2>/dev/null; do sleep 1; done
fi
