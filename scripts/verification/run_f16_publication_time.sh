#!/bin/sh
set -eu

# An isolated HTTP/PostgreSQL fixture for the publication clock contract.
task_repo=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
task_tmp=$(mktemp -d /tmp/devcoreblog-f16.XXXXXX)
task_source="$task_tmp/source"
task_pgdata="$task_tmp/postgres"
task_socket="$task_tmp/socket"
task_pg_port=${DEVCORE_F16_PG_PORT:-55433}
task_app_port=${DEVCORE_F16_APP_PORT:-15160}
task_pg_user=$(id -un)
task_db=devcoreblog_f01_test
task_admin_password=f16-isolated-password
task_app_pid=
task_pg_started=0

stop_app() {
    if [ -n "$task_app_pid" ]; then
        kill "$task_app_pid" 2>/dev/null || true
        wait "$task_app_pid" 2>/dev/null || true
        task_app_pid=
    fi
}

cleanup() {
    stop_app
    if [ "$task_pg_started" -eq 1 ]; then
        pg_ctl -D "$task_pgdata" -m fast -w stop >/dev/null 2>&1 || true
    fi
    case "$task_tmp" in
        /tmp/devcoreblog-f16.*) rm -rf -- "$task_tmp" ;;
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
    --exclude cookies.txt --exclude .DS_Store \
    "$task_repo/" "$task_source/"
for task_project in . DevCoreBlog.Core DevCoreBlog.Data DevCoreBlog.Services \
    tools/DevCoreBlog.PasswordHashTool tools/DevCoreBlog.ContentRulesTool; do
    if [ -d "$task_repo/$task_project/obj" ]; then
        mkdir -p "$task_source/$task_project/obj"
        rsync -a "$task_repo/$task_project/obj/" "$task_source/$task_project/obj/"
    fi
done
: > "$task_source/.env"

if [ "${1:-current}" = "baseline" ]; then
    for task_file in Program.cs Controllers/AdminPostController.cs \
        DevCoreBlog.Services/PostService.cs Models/Admin/PostFormInput.cs \
        Views/AdminPost/Create.cshtml Views/AdminPost/Edit.cshtml; do
        git -C "$task_repo" show "HEAD:$task_file" > "$task_source/$task_file"
    done
elif [ "${1:-current}" != "current" ]; then
    printf 'Expected baseline or current mode.\n' >&2
    exit 2
fi

(
    cd "$task_source"
    dotnet build DevCoreBlog.csproj --no-restore --nologo \
        --disable-build-servers -m:1 -p:NuGetAudit=false -nodeReuse:false
    dotnet build tools/DevCoreBlog.PasswordHashTool/DevCoreBlog.PasswordHashTool.csproj \
        --no-restore --nologo --disable-build-servers -m:1 \
        -p:NuGetAudit=false -nodeReuse:false
    if [ "${1:-current}" = "current" ]; then
        dotnet build tools/DevCoreBlog.ContentRulesTool/DevCoreBlog.ContentRulesTool.csproj \
            --no-restore --nologo --disable-build-servers -m:1 \
            -p:NuGetAudit=false -nodeReuse:false
    fi
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
createdb -h 127.0.0.1 -p "$task_pg_port" -U "$task_pg_user" "$task_db"
psql -h 127.0.0.1 -p "$task_pg_port" -U "$task_pg_user" -d "$task_db" \
    -v ON_ERROR_STOP=1 -f "$task_source/scripts/verification/f01_fixture.sql" >/dev/null

if [ "${1:-current}" = "current" ]; then
    DB_CONNECTION_STRING="Host=127.0.0.1;Port=$task_pg_port;Database=$task_db;Username=$task_pg_user" \
    dotnet run --no-build \
        --project "$task_source/tools/DevCoreBlog.ContentRulesTool/DevCoreBlog.ContentRulesTool.csproj"
fi

psql -h 127.0.0.1 -p "$task_pg_port" -U "$task_pg_user" -d "$task_db" \
    -A -t -c 'SELECT "PublishDate" FROM "Posts" WHERE "Id" = 2002' \
    > "$task_tmp/unchanged-before.txt"

start_app() {
    task_server_timezone=$1
    task_site_time_zone=$2
    task_log=$3
    (
        cd "$task_source"
        exec env -u ADMIN_PASSWORD \
            TZ="$task_server_timezone" SITE_TIME_ZONE="$task_site_time_zone" \
            ASPNETCORE_ENVIRONMENT=Development DOTNET_ENVIRONMENT=Development \
            DB_CONNECTION_STRING="Host=127.0.0.1;Port=$task_pg_port;Database=$task_db;Username=$task_pg_user" \
            ADMIN_USERNAME=f16-admin ADMIN_PASSWORD_HASH="$task_admin_password_hash" \
            CLOUDINARY_CLOUD_NAME=f16-cloud CLOUDINARY_API_KEY=f16-key \
            CLOUDINARY_API_SECRET=f16-secret WEBHOOK_API_SECRET=f16-webhook \
            dotnet run --no-build --project DevCoreBlog.csproj \
            --urls "http://127.0.0.1:$task_app_port" > "$task_log" 2>&1
    ) &
    task_app_pid=$!
}

run_probe() {
    DEVCORE_TEST_ADMIN_USERNAME=f16-admin \
    DEVCORE_TEST_ADMIN_PASSWORD="$task_admin_password" \
    python3 "$task_source/scripts/verification/f16_publication_time_probe.py" \
        --mode "$1" --marker "$2" \
        --base-url "http://127.0.0.1:$task_app_port" \
        --database-port "$task_pg_port" \
        --database-user "$task_pg_user" \
        --database-name "$task_db"
}

start_app UTC Europe/Istanbul "$task_tmp/application-utc.log"
run_probe roundtrip F16_TIME_ROUNDTRIP_UTC

stop_app
start_app Asia/Tokyo Europe/Istanbul "$task_tmp/application-tokyo.log"
run_probe roundtrip F16_TIME_ROUNDTRIP_TOKYO

stop_app
start_app UTC America/New_York "$task_tmp/application-new-york.log"
run_probe dst F16_TIME_DST

psql -h 127.0.0.1 -p "$task_pg_port" -U "$task_pg_user" -d "$task_db" \
    -A -t -c 'SELECT "PublishDate" FROM "Posts" WHERE "Id" = 2002' \
    > "$task_tmp/unchanged-after.txt"
cmp "$task_tmp/unchanged-before.txt" "$task_tmp/unchanged-after.txt"
printf 'f16_existing_utc_record_unchanged=true\n'

stop_app
start_app UTC Europe/Istanbul "$task_tmp/application-regression.log"
DEVCORE_TEST_ADMIN_USERNAME=f16-admin \
DEVCORE_TEST_ADMIN_PASSWORD="$task_admin_password" \
python3 "$task_source/scripts/verification/f11_optional_thumbnail_probe.py" \
    --base-url "http://127.0.0.1:$task_app_port"
DEVCORE_TEST_ADMIN_USERNAME=f16-admin \
DEVCORE_TEST_ADMIN_PASSWORD="$task_admin_password" \
DEVCORE_TEST_WEBHOOK_SECRET=f16-webhook \
python3 "$task_source/scripts/verification/f12_content_validation_probe.py" \
    --base-url "http://127.0.0.1:$task_app_port"

if [ "${DEVCORE_F16_HOLD_FOR_BROWSER:-0}" = "1" ]; then
    stop_app
    start_app UTC Europe/Istanbul "$task_tmp/application-browser.log"
    printf 'F16 browser fixture ready at http://127.0.0.1:%s\n' "$task_app_port"
    while kill -0 "$task_app_pid" 2>/dev/null; do
        sleep 1
    done
fi
