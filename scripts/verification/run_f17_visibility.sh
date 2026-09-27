#!/bin/sh
set -eu

# Run F17 checks only against a disposable PostgreSQL cluster and source copy.
task_repo=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
task_tmp=$(mktemp -d /tmp/devcoreblog-f17.XXXXXX)
task_source="$task_tmp/source"
task_pgdata="$task_tmp/postgres"
task_socket="$task_tmp/socket"
task_pg_port=${DEVCORE_F17_PG_PORT:-55434}
task_app_port=${DEVCORE_F17_APP_PORT:-15161}
task_pg_user=$(id -un)
task_db=devcoreblog_f01_test
task_admin_password=f17-isolated-password
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
        /tmp/devcoreblog-f17.*) rm -rf -- "$task_tmp" ;;
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

case "${1:-current}" in
    baseline)
        for task_file in Program.cs Controllers/HomeController.cs \
            Controllers/PublicFeedController.cs Controllers/SeoController.cs \
            DevCoreBlog.Data/Repositories/PostRepository.cs \
            DevCoreBlog.Data/Repositories/CategoryRepository.cs \
            DevCoreBlog.Services/PostService.cs DevCoreBlog.Services/CategoryService.cs \
            DevCoreBlog.Services/Interfaces/IPostService.cs \
            DevCoreBlog.Services/Interfaces/ICategoryService.cs; do
            git -C "$task_repo" show "HEAD:$task_file" > "$task_source/$task_file"
        done
        ;;
    current) ;;
    *) printf 'Expected baseline or current mode.\n' >&2; exit 2 ;;
esac

(
    cd "$task_source"
    dotnet build DevCoreBlog.csproj --no-restore --nologo \
        --disable-build-servers -m:1 -p:NuGetAudit=false -nodeReuse:false
    dotnet build tools/DevCoreBlog.PasswordHashTool/DevCoreBlog.PasswordHashTool.csproj \
        --no-restore --nologo --disable-build-servers -m:1 \
        -p:NuGetAudit=false -nodeReuse:false
    if [ "${1:-current}" = current ]; then
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

if [ "${1:-current}" = current ]; then
    DB_CONNECTION_STRING="Host=127.0.0.1;Port=$task_pg_port;Database=$task_db;Username=$task_pg_user" \
    dotnet run --no-build \
        --project "$task_source/tools/DevCoreBlog.ContentRulesTool/DevCoreBlog.ContentRulesTool.csproj" \
        -- --visibility
    DB_CONNECTION_STRING="Host=127.0.0.1;Port=$task_pg_port;Database=$task_db;Username=$task_pg_user" \
    dotnet run --no-build \
        --project "$task_source/tools/DevCoreBlog.ContentRulesTool/DevCoreBlog.ContentRulesTool.csproj"
fi

(
    cd "$task_source"
    exec env -u ADMIN_PASSWORD \
        TZ=UTC SITE_TIME_ZONE=Europe/Istanbul \
        ASPNETCORE_ENVIRONMENT=Development DOTNET_ENVIRONMENT=Development \
        DB_CONNECTION_STRING="Host=127.0.0.1;Port=$task_pg_port;Database=$task_db;Username=$task_pg_user" \
        ADMIN_USERNAME=f17-admin ADMIN_PASSWORD_HASH="$task_admin_password_hash" \
        CLOUDINARY_CLOUD_NAME=f17-cloud CLOUDINARY_API_KEY=f17-key \
        CLOUDINARY_API_SECRET=f17-secret WEBHOOK_API_SECRET=f17-webhook \
        dotnet run --no-build --project DevCoreBlog.csproj \
        --urls "http://127.0.0.1:$task_app_port" > "$task_tmp/application.log" 2>&1
) &
task_app_pid=$!

DEVCORE_TEST_ADMIN_USERNAME=f17-admin \
DEVCORE_TEST_ADMIN_PASSWORD="$task_admin_password" \
python3 "$task_source/scripts/verification/f17_visibility_probe.py" \
    --base-url "http://127.0.0.1:$task_app_port"

if [ "${DEVCORE_F18_PROBE:-0}" = 1 ]; then
    DEVCORE_TEST_ADMIN_USERNAME=f17-admin \
    DEVCORE_TEST_ADMIN_PASSWORD="$task_admin_password" \
    python3 "$task_source/scripts/verification/f18_cache_probe.py" \
        --base-url "http://127.0.0.1:$task_app_port" \
        --pg-port "$task_pg_port" --pg-user "$task_pg_user"
fi

if [ "${DEVCORE_F19_PROBE:-0}" = 1 ]; then
    DEVCORE_TEST_ADMIN_USERNAME=f17-admin \
    DEVCORE_TEST_ADMIN_PASSWORD="$task_admin_password" \
    python3 "$task_source/scripts/verification/f19_stable_slug_probe.py" \
        --base-url "http://127.0.0.1:$task_app_port" \
        --pg-port "$task_pg_port" --pg-user "$task_pg_user"
    DEVCORE_TEST_ADMIN_USERNAME=f17-admin \
    DEVCORE_TEST_ADMIN_PASSWORD="$task_admin_password" \
    python3 "$task_source/scripts/verification/f14_category_edit_probe.py" \
        --base-url "http://127.0.0.1:$task_app_port" \
        --database-port "$task_pg_port" --database-user "$task_pg_user" \
        --database-name "$task_db"
fi

if [ "${1:-current}" = current ] && [ "${DEVCORE_F17_HOLD_FOR_BROWSER:-0}" != 1 ]; then
    DEVCORE_TEST_ADMIN_USERNAME=f17-admin \
    DEVCORE_TEST_ADMIN_PASSWORD="$task_admin_password" \
    python3 "$task_source/scripts/verification/f11_optional_thumbnail_probe.py" \
        --base-url "http://127.0.0.1:$task_app_port"
    DEVCORE_TEST_ADMIN_USERNAME=f17-admin \
    DEVCORE_TEST_ADMIN_PASSWORD="$task_admin_password" \
    DEVCORE_TEST_WEBHOOK_SECRET=f17-webhook \
    python3 "$task_source/scripts/verification/f12_content_validation_probe.py" \
        --base-url "http://127.0.0.1:$task_app_port"
fi

if [ "${DEVCORE_F17_HOLD_FOR_BROWSER:-0}" = 1 ]; then
    printf 'F17 browser fixture ready at http://127.0.0.1:%s\n' "$task_app_port"
    while kill -0 "$task_app_pid" 2>/dev/null; do
        sleep 1
    done
fi
