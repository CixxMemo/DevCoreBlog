#!/bin/sh
set -eu

# This runner copies the current working tree, starts a temporary PostgreSQL
# cluster, and uses only synthetic credentials and content.
task_repo=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
task_tmp=$(mktemp -d /tmp/devcoreblog-f01.XXXXXX)
task_source="$task_tmp/source"
task_pgdata="$task_tmp/postgres"
task_socket="$task_tmp/socket"
task_pglog="$task_tmp/postgres.log"
task_applog="$task_tmp/application.log"
task_restart_log="$task_tmp/application-restart.log"
task_stale_log="$task_tmp/application-stale.log"
task_https_log="$task_tmp/application-https.log"
task_data_protection_keys="$task_tmp/data-protection-keys"
task_session_cookie_file="$task_tmp/admin-session.cookies"
task_tls_certificate="$task_tmp/localhost.crt"
task_tls_key="$task_tmp/localhost.key"
task_pg_port=${DEVCORE_F01_PG_PORT:-55432}
task_app_port=${DEVCORE_F01_APP_PORT:-15159}
task_db=devcoreblog_f01_test
task_f10_empty_db=devcoreblog_f10_empty_test
task_f10_legacy_db=devcoreblog_f10_legacy_test
task_pg_user=$(id -un)
task_admin_username=f02-admin
task_admin_password=f02-isolated-valid-password
task_admin_password_hash=
task_login_permit_limit=${DEVCORE_F06_PERMIT_LIMIT:-4}
task_login_window_seconds=${DEVCORE_F06_WINDOW_SECONDS:-2}
task_f06_username_marker=F06_USERNAME_MUST_NOT_BE_LOGGED
task_f06_password_marker=F06_PASSWORD_MUST_NOT_BE_LOGGED
task_pg_started=0
task_app_pid=
task_cleaned=0

stop_app() {
    if [ -n "$task_app_pid" ]; then
        kill "$task_app_pid" 2>/dev/null || true
        wait "$task_app_pid" 2>/dev/null || true
        task_app_pid=
    fi
}

cleanup() {
    if [ "$task_cleaned" -eq 1 ]; then
        return
    fi
    task_cleaned=1

    stop_app
    if [ "$task_pg_started" -eq 1 ]; then
        pg_ctl -D "$task_pgdata" -m fast -w stop >/dev/null 2>&1 || true
    fi
    if [ "${DEVCORE_F01_KEEP_TEMP:-0}" = "1" ]; then
        printf 'F01 temporary evidence directory retained: %s\n' "$task_tmp"
    else
        case "$task_tmp" in
            /tmp/devcoreblog-f01.*)
                rm -rf -- "$task_tmp"
                printf 'F01 temporary evidence directory removed.\n'
                ;;
            *)
                printf 'Unexpected temporary path; it was not removed: %s\n' "$task_tmp" >&2
                ;;
        esac
    fi
}
trap cleanup EXIT INT TERM

for task_command in dotnet rsync initdb pg_ctl createdb psql python3 openssl; do
    command -v "$task_command" >/dev/null 2>&1 || {
        printf 'Missing required command: %s\n' "$task_command" >&2
        exit 1
    }
done

pg_isready -h 127.0.0.1 -p "$task_pg_port" >/dev/null 2>&1 && {
    printf 'Port %s already has a PostgreSQL server; choose DEVCORE_F01_PG_PORT.\n' "$task_pg_port" >&2
    exit 1
}

mkdir -p "$task_source" "$task_socket" "$task_data_protection_keys"
chmod 700 "$task_data_protection_keys"
openssl req -x509 -newkey rsa:2048 -sha256 -nodes -days 1 \
    -subj "/CN=localhost" \
    -keyout "$task_tls_key" \
    -out "$task_tls_certificate" >/dev/null 2>&1
chmod 600 "$task_tls_key" "$task_tls_certificate"
rsync -a \
    --exclude .git --exclude .env --exclude bin --exclude obj \
    --exclude cookies.txt --exclude .DS_Store \
    "$task_repo/" "$task_source/"

# Existing restore metadata is copied only into the isolated tree. This avoids
# changing the user's tracked bin/obj files or downloading packages for F01.
for task_project in . DevCoreBlog.Core DevCoreBlog.Data DevCoreBlog.Services \
    tools/DevCoreBlog.PasswordHashTool tools/DevCoreBlog.ImageUploadPolicyTool; do
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
    dotnet build tools/DevCoreBlog.ImageUploadPolicyTool/DevCoreBlog.ImageUploadPolicyTool.csproj \
        --no-restore --nologo --disable-build-servers -m:1 \
        -p:NuGetAudit=false -nodeReuse:false
)

(
    cd "$task_source"
    dotnet run --no-build \
        --project tools/DevCoreBlog.PasswordHashTool/DevCoreBlog.PasswordHashTool.csproj \
        -- --self-test
    dotnet run --no-build \
        --project tools/DevCoreBlog.PasswordHashTool/DevCoreBlog.PasswordHashTool.csproj \
        -- --benchmark
    dotnet run --no-build \
        --project tools/DevCoreBlog.ImageUploadPolicyTool/DevCoreBlog.ImageUploadPolicyTool.csproj
)

task_admin_password_hash=$(
    printf '%s\n' "$task_admin_password" | (
        cd "$task_source"
        dotnet run --no-build \
            --project tools/DevCoreBlog.PasswordHashTool/DevCoreBlog.PasswordHashTool.csproj \
            -- --stdin
    )
)
if [ -z "$task_admin_password_hash" ]; then
    printf 'F07 failed to generate the isolated admin password hash.\n' >&2
    exit 1
fi

DEVCORE_TEST_ADMIN_PASSWORD_HASH="$task_admin_password_hash" \
python3 "$task_source/scripts/verification/f01_migration_probe.py" \
    --root "$task_source"

initdb -D "$task_pgdata" --no-locale --encoding=UTF8 --auth=trust >/dev/null
pg_ctl -D "$task_pgdata" -l "$task_pglog" \
    -o "-p $task_pg_port -h 127.0.0.1 -k $task_socket" -w start >/dev/null
task_pg_started=1
createdb -h 127.0.0.1 -p "$task_pg_port" -U "$task_pg_user" "$task_db"
createdb -h 127.0.0.1 -p "$task_pg_port" -U "$task_pg_user" "$task_f10_empty_db"
createdb -h 127.0.0.1 -p "$task_pg_port" -U "$task_pg_user" "$task_f10_legacy_db"

DEVCORE_TEST_ADMIN_PASSWORD_HASH="$task_admin_password_hash" \
python3 "$task_source/scripts/verification/f10_database_migration_probe.py" \
    --root "$task_source" \
    --port "$task_pg_port" \
    --user "$task_pg_user" \
    --empty-database "$task_f10_empty_db" \
    --legacy-database "$task_f10_legacy_db"

psql -h 127.0.0.1 -p "$task_pg_port" -U "$task_pg_user" -d "$task_db" \
    -v ON_ERROR_STOP=1 -f "$task_source/scripts/verification/f01_fixture.sql" >/dev/null

start_app() {
    task_environment=$1
    task_session_version=$2
    task_session_lifetime=$3
    task_scheme=$4
    task_log=$5

    if [ -n "$task_app_pid" ]; then
        printf 'An isolated application process is already running.\n' >&2
        exit 1
    fi

    (
        cd "$task_source"
        set -- env -u ADMIN_PASSWORD \
            ASPNETCORE_ENVIRONMENT="$task_environment" \
            DOTNET_ENVIRONMENT="$task_environment" \
            DB_CONNECTION_STRING="Host=127.0.0.1;Port=$task_pg_port;Database=$task_db;Username=$task_pg_user" \
            ADMIN_USERNAME="$task_admin_username" \
            ADMIN_PASSWORD_HASH="$task_admin_password_hash" \
            ADMIN_SESSION_VERSION="$task_session_version" \
            ADMIN_SESSION_LIFETIME_SECONDS="$task_session_lifetime" \
            DATA_PROTECTION_KEYS_PATH="$task_data_protection_keys" \
            CLOUDINARY_CLOUD_NAME=f01-cloud \
            CLOUDINARY_API_KEY=f01-key \
            CLOUDINARY_API_SECRET=f01-secret \
            WEBHOOK_API_SECRET=f01-webhook-secret \
            PORTFOLIO_CORS_ORIGIN=http://127.0.0.1:19999 \
            Security__LoginRateLimit__PermitLimit="$task_login_permit_limit" \
            Security__LoginRateLimit__WindowSeconds="$task_login_window_seconds"

        if [ "$task_scheme" = "https" ]; then
            set -- "$@" \
                ASPNETCORE_Kestrel__Certificates__Default__Path="$task_tls_certificate" \
                ASPNETCORE_Kestrel__Certificates__Default__KeyPath="$task_tls_key"
        fi

        exec "$@" dotnet run --no-build --project DevCoreBlog.csproj \
            --urls "$task_scheme://127.0.0.1:$task_app_port" >"$task_log" 2>&1
    ) &
    task_app_pid=$!
}

expect_admin_config_rejection() {
    task_probe_kind=$1
    task_expected_message=$2
    task_probe_log="$task_tmp/$task_probe_kind.log"

    set -- env -u ADMIN_USERNAME -u ADMIN_PASSWORD -u ADMIN_PASSWORD_HASH \
        ASPNETCORE_ENVIRONMENT=Development \
        DOTNET_ENVIRONMENT=Development \
        "DB_CONNECTION_STRING=Host=127.0.0.1;Port=$task_pg_port;Database=$task_db;Username=$task_pg_user" \
        CLOUDINARY_CLOUD_NAME=f01-cloud \
        CLOUDINARY_API_KEY=f01-key \
        CLOUDINARY_API_SECRET=f01-secret \
        WEBHOOK_API_SECRET=f01-webhook-secret \
        PORTFOLIO_CORS_ORIGIN=http://127.0.0.1:19999

    case "$task_probe_kind" in
        username-missing)
            set -- "$@" "ADMIN_PASSWORD_HASH=$task_admin_password_hash"
            ;;
        hash-missing)
            set -- "$@" "ADMIN_USERNAME=$task_admin_username"
            ;;
        username-whitespace)
            set -- "$@" "ADMIN_USERNAME=   " "ADMIN_PASSWORD_HASH=$task_admin_password_hash"
            ;;
        hash-whitespace)
            set -- "$@" "ADMIN_USERNAME=$task_admin_username" "ADMIN_PASSWORD_HASH=   "
            ;;
        hash-malformed)
            set -- "$@" "ADMIN_USERNAME=$task_admin_username" "ADMIN_PASSWORD_HASH=not-a-valid-f07-hash"
            ;;
        plaintext-only)
            set -- "$@" "ADMIN_USERNAME=$task_admin_username" "ADMIN_PASSWORD=$task_admin_password"
            ;;
        *)
            printf 'Unknown admin configuration probe: %s\n' "$task_probe_kind" >&2
            exit 1
            ;;
    esac

    (
        cd "$task_source"
        exec "$@" dotnet run --no-build --project DevCoreBlog.csproj \
            --urls "http://127.0.0.1:$task_app_port" >"$task_probe_log" 2>&1
    ) &
    task_probe_pid=$!

    task_probe_attempt=0
    while kill -0 "$task_probe_pid" 2>/dev/null && [ "$task_probe_attempt" -lt 80 ]; do
        sleep 0.1
        task_probe_attempt=$((task_probe_attempt + 1))
    done

    if kill -0 "$task_probe_pid" 2>/dev/null; then
        kill "$task_probe_pid" 2>/dev/null || true
        wait "$task_probe_pid" 2>/dev/null || true
        printf '%s configuration was incorrectly accepted.\n' "$task_probe_kind" >&2
        exit 1
    fi

    set +e
    wait "$task_probe_pid"
    task_probe_status=$?
    set -e
    if [ "$task_probe_status" -eq 0 ]; then
        printf '%s configuration exited successfully instead of failing closed.\n' "$task_probe_kind" >&2
        exit 1
    fi
    if ! grep -F "$task_expected_message" "$task_probe_log" >/dev/null; then
        printf '%s did not report the expected secret-free configuration error.\n' "$task_probe_kind" >&2
        exit 1
    fi
    if grep -F "$task_admin_password" "$task_probe_log" >/dev/null; then
        printf '%s exposed the configured test password in its log.\n' "$task_probe_kind" >&2
        exit 1
    fi
    if grep -F "$task_admin_password_hash" "$task_probe_log" >/dev/null; then
        printf '%s exposed the configured test password hash in its log.\n' "$task_probe_kind" >&2
        exit 1
    fi

    printf 'Rejected unsafe admin configuration: %s\n' "$task_probe_kind"
}

expect_admin_config_rejection username-missing \
    'ADMIN_USERNAME is required and cannot be empty or whitespace.'
expect_admin_config_rejection hash-missing \
    'ADMIN_PASSWORD_HASH is required and cannot be empty or whitespace.'
expect_admin_config_rejection username-whitespace \
    'ADMIN_USERNAME is required and cannot be empty or whitespace.'
expect_admin_config_rejection hash-whitespace \
    'ADMIN_PASSWORD_HASH is required and cannot be empty or whitespace.'
expect_admin_config_rejection hash-malformed \
    'ADMIN_PASSWORD_HASH is malformed, unsupported, or outside the allowed cost bounds.'
expect_admin_config_rejection plaintext-only \
    'ADMIN_PASSWORD_HASH is required and cannot be empty or whitespace.'

expect_data_protection_config_rejection() {
    task_probe_kind=$1
    task_probe_path=$2
    task_expected_message=$3
    task_probe_log="$task_tmp/$task_probe_kind.log"

    set -- env -u ADMIN_PASSWORD -u DATA_PROTECTION_KEYS_PATH \
        ASPNETCORE_ENVIRONMENT=Production \
        DOTNET_ENVIRONMENT=Production \
        "DB_CONNECTION_STRING=Host=127.0.0.1;Port=$task_pg_port;Database=$task_db;Username=$task_pg_user" \
        "ADMIN_USERNAME=$task_admin_username" \
        "ADMIN_PASSWORD_HASH=$task_admin_password_hash" \
        ADMIN_SESSION_VERSION=1 \
        ADMIN_SESSION_LIFETIME_SECONDS=1800 \
        CLOUDINARY_CLOUD_NAME=f01-cloud \
        CLOUDINARY_API_KEY=f01-key \
        CLOUDINARY_API_SECRET=f01-secret \
        WEBHOOK_API_SECRET=f01-webhook-secret

    if [ -n "$task_probe_path" ]; then
        set -- "$@" "DATA_PROTECTION_KEYS_PATH=$task_probe_path"
    fi

    (
        cd "$task_source"
        exec "$@" dotnet run --no-build --project DevCoreBlog.csproj \
            --urls "http://127.0.0.1:$task_app_port" >"$task_probe_log" 2>&1
    ) &
    task_probe_pid=$!

    task_probe_attempt=0
    while kill -0 "$task_probe_pid" 2>/dev/null && [ "$task_probe_attempt" -lt 80 ]; do
        sleep 0.1
        task_probe_attempt=$((task_probe_attempt + 1))
    done

    if kill -0 "$task_probe_pid" 2>/dev/null; then
        kill "$task_probe_pid" 2>/dev/null || true
        wait "$task_probe_pid" 2>/dev/null || true
        printf '%s configuration was incorrectly accepted.\n' "$task_probe_kind" >&2
        exit 1
    fi

    set +e
    wait "$task_probe_pid"
    task_probe_status=$?
    set -e
    if [ "$task_probe_status" -eq 0 ] ||
        ! grep -F "$task_expected_message" "$task_probe_log" >/dev/null; then
        printf '%s did not fail with the expected configuration error.\n' \
            "$task_probe_kind" >&2
        exit 1
    fi
    if grep -F "$task_admin_password_hash" "$task_probe_log" >/dev/null; then
        printf '%s exposed the configured test password hash.\n' \
            "$task_probe_kind" >&2
        exit 1
    fi

    printf 'Rejected unsafe Data Protection configuration: %s\n' "$task_probe_kind"
}

task_shared_key_directory="$task_tmp/shared-data-protection-keys"
mkdir -p "$task_shared_key_directory"
chmod 755 "$task_shared_key_directory"
expect_data_protection_config_rejection data-protection-path-missing "" \
    'DATA_PROTECTION_KEYS_PATH is required outside Development.'
expect_data_protection_config_rejection data-protection-path-relative relative-keys \
    'DATA_PROTECTION_KEYS_PATH must be an absolute path.'
expect_data_protection_config_rejection data-protection-path-shared \
    "$task_shared_key_directory" \
    'DATA_PROTECTION_KEYS_PATH must be accessible only to its owner in production.'

start_app Development 1 1800 http "$task_applog"

DEVCORE_TEST_ADMIN_USERNAME="$task_admin_username" \
DEVCORE_TEST_ADMIN_PASSWORD="$task_admin_password" \
python3 "$task_source/scripts/verification/f01_http_baseline.py" \
    --base-url "http://127.0.0.1:$task_app_port" \
    --expect-f02-fixed \
    --expect-f03-fixed \
    --expect-f04-fixed \
    --expect-f05-fixed

# Each phase probe gets a fresh fixed window while sharing the same isolated host.
sleep $((task_login_window_seconds + 1))

DEVCORE_TEST_ADMIN_USERNAME="$task_admin_username" \
DEVCORE_TEST_ADMIN_PASSWORD="$task_admin_password" \
DEVCORE_TEST_WEBHOOK_SECRET=f01-webhook-secret \
python3 "$task_source/scripts/verification/f05_antiforgery_probe.py" \
    --base-url "http://127.0.0.1:$task_app_port"

sleep $((task_login_window_seconds + 1))

DEVCORE_TEST_ADMIN_USERNAME="$task_admin_username" \
DEVCORE_TEST_ADMIN_PASSWORD="$task_admin_password" \
DEVCORE_F06_USERNAME_MARKER="$task_f06_username_marker" \
DEVCORE_F06_PASSWORD_MARKER="$task_f06_password_marker" \
python3 "$task_source/scripts/verification/f06_login_rate_limit_probe.py" \
    --base-url "http://127.0.0.1:$task_app_port" \
    --permit-limit "$task_login_permit_limit" \
    --window-seconds "$task_login_window_seconds"

if grep -F "$task_f06_username_marker" "$task_applog" >/dev/null; then
    printf 'F06 application log exposed the submitted username marker.\n' >&2
    exit 1
fi
if grep -F "$task_f06_password_marker" "$task_applog" >/dev/null; then
    printf 'F06 application log exposed the submitted password marker.\n' >&2
    exit 1
fi
if grep -F "$task_admin_password" "$task_applog" >/dev/null; then
    printf 'F07 application log exposed the submitted test password.\n' >&2
    exit 1
fi
if grep -F "$task_admin_password_hash" "$task_applog" >/dev/null; then
    printf 'F07 application log exposed the configured test password hash.\n' >&2
    exit 1
fi
printf 'F07 login uses only the versioned hash configuration without logging secrets.\n'
if ! grep -F 'Admin sign-in attempt failed from direct connection IP' "$task_applog" >/dev/null; then
    printf 'F06 did not record failed sign-in events.\n' >&2
    exit 1
fi
if ! grep -F 'Admin sign-in rate limit rejected a request from direct connection IP' "$task_applog" >/dev/null; then
    printf 'F06 did not record rate-limit rejections.\n' >&2
    exit 1
fi
printf 'F06 sign-in logs contain event metadata without submitted credentials.\n'

sleep $((task_login_window_seconds + 1))

DEVCORE_TEST_ADMIN_USERNAME="$task_admin_username" \
DEVCORE_TEST_ADMIN_PASSWORD="$task_admin_password" \
python3 "$task_source/scripts/verification/f09_image_upload_probe.py" \
    --base-url "http://127.0.0.1:$task_app_port"

sleep $((task_login_window_seconds + 1))

DEVCORE_TEST_ADMIN_USERNAME="$task_admin_username" \
DEVCORE_TEST_ADMIN_PASSWORD="$task_admin_password" \
python3 "$task_source/scripts/verification/f08_session_probe.py" \
    --mode issue \
    --base-url "http://127.0.0.1:$task_app_port" \
    --cookie-file "$task_session_cookie_file"

stop_app
start_app Development 1 1800 http "$task_restart_log"

DEVCORE_TEST_ADMIN_USERNAME="$task_admin_username" \
DEVCORE_TEST_ADMIN_PASSWORD="$task_admin_password" \
python3 "$task_source/scripts/verification/f08_session_probe.py" \
    --mode current \
    --base-url "http://127.0.0.1:$task_app_port" \
    --cookie-file "$task_session_cookie_file"

stop_app
start_app Development 2 1800 http "$task_stale_log"

DEVCORE_TEST_ADMIN_USERNAME="$task_admin_username" \
DEVCORE_TEST_ADMIN_PASSWORD="$task_admin_password" \
python3 "$task_source/scripts/verification/f08_session_probe.py" \
    --mode stale \
    --base-url "http://127.0.0.1:$task_app_port" \
    --cookie-file "$task_session_cookie_file"

if ! grep -F 'Rejected an admin authentication ticket with a missing or stale session version.' \
    "$task_stale_log" >/dev/null; then
    printf 'F08 did not record stale session rejection.\n' >&2
    exit 1
fi

stop_app
start_app Production 2 4 https "$task_https_log"

DEVCORE_TEST_ADMIN_USERNAME="$task_admin_username" \
DEVCORE_TEST_ADMIN_PASSWORD="$task_admin_password" \
python3 "$task_source/scripts/verification/f08_session_probe.py" \
    --mode expiry \
    --base-url "https://127.0.0.1:$task_app_port" \
    --lifetime-seconds 4 \
    --expect-secure

for task_secret_log in "$task_restart_log" "$task_stale_log" "$task_https_log"; do
    if grep -F "$task_admin_password" "$task_secret_log" >/dev/null; then
        printf 'F08 application log exposed the submitted test password.\n' >&2
        exit 1
    fi
    if grep -F "$task_admin_password_hash" "$task_secret_log" >/dev/null; then
        printf 'F08 application log exposed the configured test password hash.\n' >&2
        exit 1
    fi
done
printf 'F08 restart, revocation, expiry, and HTTPS cookie checks passed without logged secrets.\n'

if [ "${DEVCORE_F01_HOLD_FOR_BROWSER:-0}" = "1" ]; then
    stop_app
    start_app Development 2 1800 http "$task_tmp/application-browser.log"
    sleep $((task_login_window_seconds + 1))
    printf 'Browser fixture ready at http://127.0.0.1:%s\n' "$task_app_port"
    printf 'Press Ctrl-C after browser verification to stop and clean the fixture.\n'
    while kill -0 "$task_app_pid" 2>/dev/null; do
        sleep 1
    done
    printf 'Application stopped before browser verification completed.\n' >&2
    exit 1
fi
