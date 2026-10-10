#!/bin/sh
set -eu

# Explicit synthetic configuration shared with the HTTP probes; no real dotenv path.
export ADMIN_LOGIN_PATH=/fixture-admin/login
export DEVCORE_TEST_ADMIN_LOGIN_PATH=/fixture-admin/login

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
task_ef_command_log_level=Warning
task_webhook_rate_limit=5
task_login_rate_limit=5
if [ -n "${DEVCORE_VOL1_F01_REPORT_DIR:-}" ] || [ -n "${DEVCORE_VOL1_F08_REPORT_DIR:-}" ]; then
    # Multiple authenticated document/privacy probes share this synthetic fixture.
    # Actual rate-limit acceptance runs in its own fresh process.
    task_login_rate_limit=20
fi
if [ "${DEVCORE_F25_PROBE:-0}" = 1 ] || [ "${DEVCORE_F29_PROBE:-0}" = 1 ]; then
    task_ef_command_log_level=Information
fi
if [ -n "${DEVCORE_F27_PROBE:-}" ] || [ "${DEVCORE_F28_PROBE:-0}" = 1 ]; then
    task_webhook_rate_limit=20
fi
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

if [ "${DEVCORE_F19_PROBE:-0}" = 1 ] &&
   [ "${DEVCORE_F21_PROBE:-0}" = 1 ]; then
    printf 'Run F19 and F21 probes separately to stay within the fixture login limit.\n' >&2
    exit 2
fi

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
mkdir -m 700 "$task_tmp/keys"
export DATA_PROTECTION_KEYS_PATH="$task_tmp/keys"
rsync -a --exclude .local --exclude .kilo --exclude .codex --exclude .auth --exclude .git --exclude '.env*' --exclude node_modules --exclude bin --exclude obj \
    --exclude cookies.txt --exclude .DS_Store \
    "$task_repo/" "$task_source/"
for task_project in . DevCoreBlog.Core DevCoreBlog.Data DevCoreBlog.Services \
    tools/DevCoreBlog.PasswordHashTool tools/DevCoreBlog.ContentRulesTool \
    tools/DevCoreBlog.ImageUploadPolicyTool; do
    if [ -d "$task_repo/$task_project/obj" ]; then
        mkdir -p "$task_source/$task_project/obj"
        rsync -a "$task_repo/$task_project/obj/" "$task_source/$task_project/obj/"
    fi
done
: > "$task_source/.env"
if [ -n "${DEVCORE_VOL1_F04_REPORT_DIR:-}" ]; then
    python3 "$task_source/scripts/verification/f04_prepare_fixture.py" --source "$task_source"
fi
if [ -n "${DEVCORE_VOL1_F08_REPORT_DIR:-}" ]; then
    python3 "$task_source/scripts/verification/f08_prepare_fixture.py" --source "$task_source"
fi
if [ "${DEVCORE_F30_PROBE:-0}" = 1 ]; then
    python3 "$task_source/scripts/verification/f30_prepare_fixture.py" --source "$task_source"
fi

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
        if [ "${DEVCORE_F26_PROBE:-0}" = 1 ]; then
            dotnet build tools/DevCoreBlog.ImageUploadPolicyTool/DevCoreBlog.ImageUploadPolicyTool.csproj \
                --no-restore --nologo --disable-build-servers -m:1 \
                -p:NuGetAudit=false -nodeReuse:false
        fi
    fi
)

if [ "${DEVCORE_F29_PROBE:-0}" = 1 ]; then
    python3 "$task_source/scripts/verification/f29_configuration_probe.py" --source "$task_source"
fi

if [ "${DEVCORE_F26_PROBE:-0}" = 1 ]; then
    if rg -q 'UnavailableImageStorage' "$task_source/Program.cs"; then
        # F53 keeps diagnostics online; the valid-upload rejection now proves fail-closed storage.
        dotnet "$task_source/tools/DevCoreBlog.ImageUploadPolicyTool/bin/Debug/net10.0/DevCoreBlog.ImageUploadPolicyTool.dll"
    else
    if (
        cd "$task_source"
        env -u CLOUDINARY_API_SECRET \
            DB_CONNECTION_STRING="Host=127.0.0.1;Port=$task_pg_port;Database=$task_db;Username=$task_pg_user" \
            CLOUDINARY_CLOUD_NAME=f17-cloud CLOUDINARY_API_KEY=f17-key \
            dotnet run --no-build --project DevCoreBlog.csproj \
                --urls "http://127.0.0.1:$task_app_port"
    ) > "$task_tmp/missing-cloudinary.log" 2>&1; then
        printf 'f26_missing_cloudinary_config_fails_closed=false\n' >&2
        exit 1
    fi
    if ! grep -Fq 'Cloudinary credentials are required for image storage.' \
        "$task_tmp/missing-cloudinary.log"; then
        printf 'f26_missing_cloudinary_config_fails_closed=false\n' >&2
        exit 1
    fi
    if grep -Fq 'f17-key' "$task_tmp/missing-cloudinary.log"; then
        printf 'f26_cloudinary_config_error_hides_credentials=false\n' >&2
        exit 1
    fi
    printf 'f26_missing_cloudinary_config_fails_closed=true\n'
    printf 'f26_cloudinary_config_error_hides_credentials=true\n'
    fi
fi

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
if [ "${DEVCORE_F44_PROBE:-0}" = 1 ] || [ "${DEVCORE_F49_PROBE:-0}" = 1 ] || [ -n "${DEVCORE_VOL1_F04_REPORT_DIR:-}" ] || [ -n "${DEVCORE_VOL1_F08_REPORT_DIR:-}" ]; then
    # Verify both a clean migration chain and the immediately preceding schema with synthetic rows.
    createdb -h 127.0.0.1 -p "$task_pg_port" -U "$task_pg_user" f44_empty
    (
        cd "$task_source"
        for task_migration_db in f44_empty "$task_db"; do
            task_connection="Host=127.0.0.1;Port=$task_pg_port;Database=$task_migration_db;Username=$task_pg_user"
            task_target=
            if [ "$task_migration_db" = "$task_db" ]; then
                task_target=20260930094820_WebhookReceipts
                if [ "${DEVCORE_F49_PROBE:-0}" = 1 ]; then task_target=20261004180016_ReadOrderingIndexes; fi
                if [ -n "${DEVCORE_VOL1_F04_REPORT_DIR:-}" ]; then task_target=20261004183254_CoverMetadata; fi
                if [ -n "${DEVCORE_VOL1_F08_REPORT_DIR:-}" ]; then task_target=20261010065908_ContentAccess; fi
            fi
            DB_CONNECTION_STRING="$task_connection" dotnet ef database update $task_target \
                --no-build --project DevCoreBlog.csproj --startup-project DevCoreBlog.csproj \
                --context ApplicationDbContext --connection "$task_connection" --no-color
        done
    )
    sed -n '/^INSERT INTO "Categories"/,$p' "$task_source/scripts/verification/f01_fixture.sql" > "$task_tmp/seed.sql"
    psql -X -h 127.0.0.1 -p "$task_pg_port" -U "$task_pg_user" -d "$task_db" \
        -v ON_ERROR_STOP=1 -f "$task_tmp/seed.sql" >/dev/null
    task_snapshot_sql='SELECT to_jsonb(p) - $$UpdatedDate$$ - $$ThumbnailAlt$$ - $$ThumbnailPublicId$$ - $$ThumbnailWidth$$ - $$ThumbnailHeight$$ - $$ContentKind$$ - $$AccessScope$$  - $$DocumentVersion$$ - $$DocumentJson$$ - $$DocumentPlainText$$ - $$DocumentWordCount$$ - $$DocumentReadingMinutes$$ FROM "Posts" p ORDER BY "Id"; SELECT to_jsonb(c) FROM "Categories" c ORDER BY "Id";'
    if [ -n "${DEVCORE_VOL1_F08_REPORT_DIR:-}" ]; then
        task_snapshot_sql='SELECT to_jsonb(p) - $$DocumentVersion$$ - $$DocumentJson$$ - $$DocumentPlainText$$ - $$DocumentWordCount$$ - $$DocumentReadingMinutes$$ FROM "Posts" p ORDER BY "Id"; SELECT to_jsonb(c) FROM "Categories" c ORDER BY "Id";'
        task_before_document_columns=$(psql -X -h 127.0.0.1 -p "$task_pg_port" -U "$task_pg_user" -d "$task_db" -At -c "SELECT count(*) FROM information_schema.columns WHERE table_name = 'Posts' AND column_name LIKE 'Document%';")
        [ "$task_before_document_columns" = 0 ]
        printf 'f08_prior_schema_has_no_document_storage=true\n'
    fi
    if [ -n "${DEVCORE_VOL1_F04_REPORT_DIR:-}" ]; then
        # F04 must preserve every prior field, including cover metadata, dates and concurrency.
        task_snapshot_sql='SELECT to_jsonb(p) - $$ContentKind$$ - $$AccessScope$$  - $$DocumentVersion$$ - $$DocumentJson$$ - $$DocumentPlainText$$ - $$DocumentWordCount$$ - $$DocumentReadingMinutes$$ FROM "Posts" p ORDER BY "Id"; SELECT to_jsonb(c) FROM "Categories" c ORDER BY "Id";'
    fi
    psql -X -h 127.0.0.1 -p "$task_pg_port" -U "$task_pg_user" -d "$task_db" \
        -At -c "$task_snapshot_sql" > "$task_tmp/prior-data.txt"
    (
        cd "$task_source"
        task_connection="Host=127.0.0.1;Port=$task_pg_port;Database=$task_db;Username=$task_pg_user"
        DB_CONNECTION_STRING="$task_connection" dotnet ef database update --no-build \
            --project DevCoreBlog.csproj --startup-project DevCoreBlog.csproj \
            --context ApplicationDbContext --connection "$task_connection" --no-color
        DB_CONNECTION_STRING="$task_connection" dotnet ef migrations has-pending-model-changes \
            --no-build --project DevCoreBlog.csproj --startup-project DevCoreBlog.csproj --context ApplicationDbContext
    )
    psql -X -h 127.0.0.1 -p "$task_pg_port" -U "$task_pg_user" -d "$task_db" \
        -At -c "$task_snapshot_sql" > "$task_tmp/upgraded-data.txt"
    cmp "$task_tmp/prior-data.txt" "$task_tmp/upgraded-data.txt"
    task_backfill_count=$(psql -X -h 127.0.0.1 -p "$task_pg_port" -U "$task_pg_user" -d "$task_db" \
        -At -c 'SELECT count(*) FROM "Posts" WHERE "UpdatedDate" IS NOT NULL;')
    [ "$task_backfill_count" = 0 ]
    task_empty_count=$(psql -X -h 127.0.0.1 -p "$task_pg_port" -U "$task_pg_user" -d f44_empty \
        -At -c 'SELECT count(*) FROM "Posts";')
    [ "$task_empty_count" = 0 ]
    task_access_default_count=$(psql -X -h 127.0.0.1 -p "$task_pg_port" -U "$task_pg_user" -d "$task_db" -At -c 'SELECT count(*) FROM "Posts" WHERE "ContentKind" <> 0 OR "AccessScope" <> 0;')
    [ "$task_access_default_count" = 0 ]
    printf 'f04_legacy_kind_and_access_mapping=true\n'
    if [ -n "${DEVCORE_VOL1_F08_REPORT_DIR:-}" ]; then
        task_document_count=$(psql -X -h 127.0.0.1 -p "$task_pg_port" -U "$task_pg_user" -d "$task_db" -At -c 'SELECT count(*) FROM "Posts" WHERE "DocumentVersion" IS NOT NULL OR "DocumentJson" IS NOT NULL OR "DocumentPlainText" IS NOT NULL OR "DocumentWordCount" IS NOT NULL OR "DocumentReadingMinutes" IS NOT NULL;')
        [ "$task_document_count" = 0 ]
        printf 'f08_blank_and_contentaccess_upgrade_preserve_every_prior_field=true\nf08_legacy_document_set_is_null=true\n'
        DEVCORE_DOCUMENT_FIXTURE_ROOT="$task_tmp" DB_CONNECTION_STRING="Host=127.0.0.1;Port=$task_pg_port;Database=$task_db;Username=$task_pg_user" \
            dotnet "$task_source/tools/DevCoreBlog.ContentRulesTool/bin/Debug/net10.0/DevCoreBlog.ContentRulesTool.dll" --documents
    fi
    if [ "${DEVCORE_F49_PROBE:-0}" = 1 ]; then
        task_media_count=$(psql -X -h 127.0.0.1 -p "$task_pg_port" -U "$task_pg_user" -d "$task_db" -At -c 'SELECT count(*) FROM "Posts" WHERE "ThumbnailPublicId" IS NOT NULL OR "ThumbnailWidth" IS NOT NULL OR "ThumbnailHeight" IS NOT NULL OR "ThumbnailAlt" IS NOT NULL;')
        [ "$task_media_count" = 0 ]
        printf 'f49_empty_and_immediate_prior_migration=true\nf49_nullable_legacy_metadata=true\n'
    fi
    printf 'f44_empty_database_migration=true\nf44_prior_data_preserved=true\nf44_no_invented_backfill=true\n'
elif [ "${DEVCORE_F28_PROBE:-0}" = 1 ]; then
    createdb -h 127.0.0.1 -p "$task_pg_port" -U "$task_pg_user" f28_empty
    (
        cd "$task_source"
        for task_migration_db in f28_empty "$task_db"; do
            task_connection="Host=127.0.0.1;Port=$task_pg_port;Database=$task_migration_db;Username=$task_pg_user"
            task_target=
            if [ "$task_migration_db" = "$task_db" ]; then task_target=20260928073947_EditVersions; fi
            DB_CONNECTION_STRING="$task_connection" dotnet ef database update $task_target \
                --no-build --project DevCoreBlog.csproj --startup-project DevCoreBlog.csproj \
                --context ApplicationDbContext --connection "$task_connection" --no-color
        done
    )
    # Reuse only the synthetic inserts, preserving the actual migrated schema.
    sed -n '/^INSERT INTO "Categories"/,$p' "$task_source/scripts/verification/f01_fixture.sql" > "$task_tmp/seed.sql"
    psql -X -h 127.0.0.1 -p "$task_pg_port" -U "$task_pg_user" -d "$task_db" \
        -v ON_ERROR_STOP=1 -f "$task_tmp/seed.sql" >/dev/null
    task_snapshot_sql='SELECT to_jsonb(p) - $$ContentKind$$ - $$AccessScope$$ - $$UpdatedDate$$ - $$ThumbnailAlt$$ - $$ThumbnailPublicId$$ - $$ThumbnailWidth$$ - $$ThumbnailHeight$$  - $$DocumentVersion$$ - $$DocumentJson$$ - $$DocumentPlainText$$ - $$DocumentWordCount$$ - $$DocumentReadingMinutes$$ FROM "Posts" p ORDER BY "Id"; SELECT to_jsonb(c) FROM "Categories" c ORDER BY "Id";'
    psql -X -h 127.0.0.1 -p "$task_pg_port" -U "$task_pg_user" -d "$task_db" \
        -At -c "$task_snapshot_sql" > "$task_tmp/prior-data.txt"
    (
        cd "$task_source"
        task_connection="Host=127.0.0.1;Port=$task_pg_port;Database=$task_db;Username=$task_pg_user"
        DB_CONNECTION_STRING="$task_connection" dotnet ef database update --no-build \
            --project DevCoreBlog.csproj --startup-project DevCoreBlog.csproj \
            --context ApplicationDbContext --connection "$task_connection" --no-color
    )
    psql -X -h 127.0.0.1 -p "$task_pg_port" -U "$task_pg_user" -d "$task_db" \
        -At -c "$task_snapshot_sql" > "$task_tmp/upgraded-data.txt"
    cmp "$task_tmp/prior-data.txt" "$task_tmp/upgraded-data.txt"
    printf 'f28_prior_migration_preserves_data=true\n'
    task_empty_count=$(psql -X -h 127.0.0.1 -p "$task_pg_port" -U "$task_pg_user" \
        -d f28_empty -At -c 'SELECT count(*) FROM "WebhookReceipts";')
    [ "$task_empty_count" = 0 ]
    printf 'f28_empty_database_migration=true\n'
else
psql -h 127.0.0.1 -p "$task_pg_port" -U "$task_pg_user" -d "$task_db" \
    -v ON_ERROR_STOP=1 -f "$task_source/scripts/verification/f01_fixture.sql" >/dev/null
psql -X -h 127.0.0.1 -p "$task_pg_port" -U "$task_pg_user" -d "$task_db" \
    -v ON_ERROR_STOP=1 -c 'ALTER TABLE "Posts" ADD COLUMN "EditVersion" bigint NOT NULL DEFAULT 1; ALTER TABLE "Posts" ADD COLUMN "ThumbnailPublicId" varchar(255), ADD COLUMN "ThumbnailWidth" integer, ADD COLUMN "ThumbnailHeight" integer, ADD COLUMN "ThumbnailAlt" varchar(300); ALTER TABLE "Categories" ADD COLUMN "EditVersion" bigint NOT NULL DEFAULT 1;' >/dev/null

fi

if [ "${1:-current}" = current ]; then
    DB_CONNECTION_STRING="Host=127.0.0.1;Port=$task_pg_port;Database=$task_db;Username=$task_pg_user" \
    dotnet run --no-build \
        --project "$task_source/tools/DevCoreBlog.ContentRulesTool/DevCoreBlog.ContentRulesTool.csproj" \
        -- --visibility
    DB_CONNECTION_STRING="Host=127.0.0.1;Port=$task_pg_port;Database=$task_db;Username=$task_pg_user" \
    dotnet run --no-build \
        --project "$task_source/tools/DevCoreBlog.ContentRulesTool/DevCoreBlog.ContentRulesTool.csproj"
fi

if [ -n "${DEVCORE_VOL1_F04_REPORT_DIR:-}" ]; then
    DB_CONNECTION_STRING="Host=127.0.0.1;Port=$task_pg_port;Database=$task_db;Username=$task_pg_user" \
    dotnet run --no-build --project "$task_source/tools/DevCoreBlog.ContentRulesTool/DevCoreBlog.ContentRulesTool.csproj" -- --access
    task_connection="Host=127.0.0.1;Port=$task_pg_port;Database=$task_db;Username=$task_pg_user"
    # The down migration must refuse to discard newly classified/private records.
    psql -X -h 127.0.0.1 -p "$task_pg_port" -U "$task_pg_user" -d "$task_db" -At \
        -c 'SELECT to_jsonb(p) FROM "Posts" p ORDER BY "Id"; SELECT to_jsonb(c) FROM "Categories" c ORDER BY "Id";' > "$task_tmp/access-before-down.txt"
    if (
        cd "$task_source"
        DB_CONNECTION_STRING="$task_connection" dotnet ef database update 20261004183254_CoverMetadata \
            --no-build --project DevCoreBlog.csproj --startup-project DevCoreBlog.csproj \
            --context ApplicationDbContext --connection "$task_connection" --no-color
    ) > "$task_tmp/refused-down.log" 2>&1; then
        printf 'f04_private_data_downgrade_refused=false\n' >&2
        exit 1
    fi
    task_private_count=$(psql -X -h 127.0.0.1 -p "$task_pg_port" -U "$task_pg_user" -d "$task_db" -At -c 'SELECT count(*) FROM "Posts" WHERE "AccessScope"=1;')
    [ "$task_private_count" = 5 ]
    # EF may commit a newer, empty additive downgrade before an earlier guard refuses.
    # Restore the current model by forward migration before running HTTP acceptance.
    (
        cd "$task_source"
        DB_CONNECTION_STRING="$task_connection" dotnet ef database update --no-build \
            --project DevCoreBlog.csproj --startup-project DevCoreBlog.csproj \
            --context ApplicationDbContext --connection "$task_connection" --no-color
    )
    psql -X -h 127.0.0.1 -p "$task_pg_port" -U "$task_pg_user" -d "$task_db" -At \
        -c 'SELECT to_jsonb(p) FROM "Posts" p ORDER BY "Id"; SELECT to_jsonb(c) FROM "Categories" c ORDER BY "Id";' > "$task_tmp/access-after-down.txt"
    cmp "$task_tmp/access-before-down.txt" "$task_tmp/access-after-down.txt"
    printf 'f04_private_data_downgrade_refused_and_preserved=true\n'
fi

start_app() {
(
    cd "$task_source"
    exec env -u ADMIN_PASSWORD \
        TZ=UTC SITE_TIME_ZONE=Europe/Istanbul \
        SITE_URL="${DEVCORE_F29_SITE_URL:-https://blog.example.test}" \
        PORTFOLIO_CORS_ORIGIN="${DEVCORE_F29_CORS_ORIGIN:-https://portfolio.example.test}" \
        ASPNETCORE_ENVIRONMENT=Development DOTNET_ENVIRONMENT=Development \
        DB_CONNECTION_STRING="Host=127.0.0.1;Port=$task_pg_port;Database=$task_db;Username=$task_pg_user" \
        ADMIN_USERNAME=f17-admin ADMIN_PASSWORD_HASH="$task_admin_password_hash" \
        CLOUDINARY_CLOUD_NAME=f17-cloud CLOUDINARY_API_KEY=f17-key \
        CLOUDINARY_API_SECRET=f17-secret \
        WEBHOOK_API_SECRET="${DEVCORE_F27_WEBHOOK_SECRET-f17-webhook}" \
        ALLOW_WEBHOOK_PUBLISH="${DEVCORE_F27_ALLOW_PUBLISH:-false}" \
        Security__WebhookRateLimit__PermitLimit="$task_webhook_rate_limit" \
        Security__LoginRateLimit__PermitLimit="$task_login_rate_limit" \
        "Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command=$task_ef_command_log_level" \
        dotnet run --no-build --project DevCoreBlog.csproj \
        --urls "http://127.0.0.1:$task_app_port" > "$task_tmp/application.log" 2>&1
) &
task_app_pid=$!
}
start_app

DEVCORE_TEST_ADMIN_USERNAME=f17-admin \
DEVCORE_TEST_ADMIN_PASSWORD="$task_admin_password" \
python3 "$task_source/scripts/verification/f17_visibility_probe.py" \
    --base-url "http://127.0.0.1:$task_app_port"

if [ -n "${DEVCORE_VOL1_F04_REPORT_DIR:-}" ]; then
    DEVCORE_TEST_ADMIN_USERNAME=f17-admin DEVCORE_TEST_ADMIN_PASSWORD="$task_admin_password" \
    python3 "$task_source/scripts/verification/f04_public_access_probe.py" \
        --base-url "http://127.0.0.1:$task_app_port" --source "$task_source" \
        --database-port "$task_pg_port" --report "$DEVCORE_VOL1_F04_REPORT_DIR/public-access.json"
fi

if [ -n "${DEVCORE_VOL1_F01_REPORT_DIR:-}" ]; then
    DEVCORE_TEST_ADMIN_USERNAME=f17-admin \
    DEVCORE_TEST_ADMIN_PASSWORD="$task_admin_password" \
    python3 "$task_source/scripts/verification/f01_admin_boundary_probe.py" \
        --source "$task_source" --database-port "$task_pg_port" \
        --base-url "http://127.0.0.1:$task_app_port" \
        --report "$DEVCORE_VOL1_F01_REPORT_DIR/admin-boundary.json"
fi

if [ "${DEVCORE_F26_PROBE:-0}" = 1 ]; then
    python3 "$task_source/scripts/verification/f26_renderer_probe.py" \
        --base-url "http://127.0.0.1:$task_app_port"
    (
        cd "$task_source"
        dotnet run --no-build \
            --project tools/DevCoreBlog.ImageUploadPolicyTool/DevCoreBlog.ImageUploadPolicyTool.csproj
    )
    DEVCORE_TEST_ADMIN_USERNAME=f17-admin \
    DEVCORE_TEST_ADMIN_PASSWORD="$task_admin_password" \
    python3 "$task_source/scripts/verification/f09_image_upload_probe.py" \
        --base-url "http://127.0.0.1:$task_app_port"
fi

if [ "${DEVCORE_F25_PROBE:-0}" = 1 ]; then
    DEVCORE_TEST_ADMIN_USERNAME=f17-admin \
    DEVCORE_TEST_ADMIN_PASSWORD="$task_admin_password" \
    python3 "$task_source/scripts/verification/f25_dashboard_probe.py" \
        --base-url "http://127.0.0.1:$task_app_port" \
        --application-log "$task_tmp/application.log" \
        --pg-port "$task_pg_port" --pg-user "$task_pg_user"
fi

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

if [ "${DEVCORE_F21_PROBE:-0}" = 1 ]; then
    DEVCORE_TEST_ADMIN_USERNAME=f17-admin \
    DEVCORE_TEST_ADMIN_PASSWORD="$task_admin_password" \
    python3 "$task_source/scripts/verification/f21_view_count_probe.py" \
        --base-url "http://127.0.0.1:$task_app_port" \
        --pg-port "$task_pg_port" --pg-user "$task_pg_user"
fi

if [ "${DEVCORE_F15_PROBE:-0}" = 1 ]; then
    DEVCORE_TEST_ADMIN_USERNAME=f17-admin \
    DEVCORE_TEST_ADMIN_PASSWORD="$task_admin_password" \
    python3 "$task_source/scripts/verification/f15_category_delete_probe.py" \
        --base-url "http://127.0.0.1:$task_app_port" \
        --database-port "$task_pg_port" --database-user "$task_pg_user" \
        --database-name "$task_db"
fi

if [ "${1:-current}" = current ] && { [ "${DEVCORE_F17_HOLD_FOR_BROWSER:-0}" != 1 ] || [ "${DEVCORE_F28_PROBE:-0}" = 1 ]; }; then
    DEVCORE_TEST_ADMIN_USERNAME=f17-admin \
    DEVCORE_TEST_ADMIN_PASSWORD="$task_admin_password" \
    python3 "$task_source/scripts/verification/f11_optional_thumbnail_probe.py" \
        --base-url "http://127.0.0.1:$task_app_port"
    if [ "${DEVCORE_F27_PROBE:-}" != missing ]; then
        DEVCORE_TEST_ADMIN_USERNAME=f17-admin \
        DEVCORE_TEST_ADMIN_PASSWORD="$task_admin_password" \
        DEVCORE_TEST_WEBHOOK_SECRET=f17-webhook \
        python3 "$task_source/scripts/verification/f12_content_validation_probe.py" \
            --base-url "http://127.0.0.1:$task_app_port"
    fi
fi

if [ "${DEVCORE_F28_PROBE:-0}" = 1 ]; then
    python3 "$task_source/scripts/verification/f28_webhook_probe.py" \
        --base-url "http://127.0.0.1:$task_app_port" --mode acceptance \
        --pg-port "$task_pg_port" --pg-user "$task_pg_user"
    kill "$task_app_pid"
    wait "$task_app_pid" || true
    start_app
    python3 "$task_source/scripts/verification/f28_webhook_probe.py" \
        --base-url "http://127.0.0.1:$task_app_port" --mode restart \
        --pg-port "$task_pg_port" --pg-user "$task_pg_user"
fi

if [ -n "${DEVCORE_F27_PROBE:-}" ]; then
    case "$DEVCORE_F27_PROBE" in
        disabled|enabled|missing) ;;
        *) printf 'Expected DEVCORE_F27_PROBE=disabled, enabled or missing.\n' >&2; exit 2 ;;
    esac
    python3 "$task_source/scripts/verification/f27_webhook_probe.py" \
        --base-url "http://127.0.0.1:$task_app_port" \
        --mode "$DEVCORE_F27_PROBE" \
        --pg-port "$task_pg_port" --pg-user "$task_pg_user"
fi

if [ "${DEVCORE_F29_PROBE:-0}" = 1 ]; then
    python3 "$task_source/scripts/verification/f29_portfolio_probe.py" \
        --mode acceptance --base-url "http://127.0.0.1:$task_app_port" \
        --pg-port "$task_pg_port" --pg-user "$task_pg_user" \
        --application-log "$task_tmp/application.log"
fi

if [ "${DEVCORE_F50_PROBE:-0}" = 1 ]; then
    python3 "$task_source/scripts/verification/f50_proxy_probe.py" \
        --source "$task_source" --database-port "$task_pg_port" \
        --report "$task_tmp/f50-proxy.json"
fi

if [ "${DEVCORE_F56_PROBE:-0}" = 1 ]; then
    DEVCORE_F56_REPO="$task_repo" python3 "$task_source/scripts/verification/f56_restore_probe.py" \
        --source "$task_source" --database-port "$task_pg_port" \
        --report "$DEVCORE_F56_REPORT"
fi

if [ "${DEVCORE_F57_PROBE:-0}" = 1 ]; then
    python3 "$task_source/scripts/verification/f57_regression_probe.py" \
        --source "$task_source" --database-port "$task_pg_port" \
        --report "$DEVCORE_F57_REPORT"
fi

if [ "${DEVCORE_F55_PROBE:-0}" = 1 ]; then
    DEVCORE_TEST_ADMIN_USERNAME=f17-admin \
    DEVCORE_TEST_ADMIN_PASSWORD="$task_admin_password" \
    python3 "$task_source/scripts/verification/f55_fixture_checks.py" \
        --source "$task_source" --database-port "$task_pg_port" \
        --base-url "http://127.0.0.1:$task_app_port" \
        --report-dir "$DEVCORE_F55_REPORT_DIR"
fi

if [ -n "${DEVCORE_VOL1_F08_REPORT_DIR:-}" ]; then
    DEVCORE_TEST_ADMIN_USERNAME=f17-admin DEVCORE_TEST_ADMIN_PASSWORD="$task_admin_password" \
    python3 "$task_source/scripts/verification/document_persistence_http_probe.py" \
        --base-url "http://127.0.0.1:$task_app_port" --database-port "$task_pg_port" \
        --report "$DEVCORE_VOL1_F08_REPORT_DIR/document-persistence-http.json"
    DEVCORE_TEST_ADMIN_USERNAME=f17-admin DEVCORE_TEST_ADMIN_PASSWORD="$task_admin_password" \
    python3 "$task_source/scripts/verification/document_writing_http_probe.py" \
        --base-url "http://127.0.0.1:$task_app_port" --database-port "$task_pg_port" \
        --report "$DEVCORE_VOL1_F08_REPORT_DIR/document-writing-http.json"
    DEVCORE_TEST_ADMIN_USERNAME=f17-admin DEVCORE_TEST_ADMIN_PASSWORD="$task_admin_password" \
    python3 "$task_source/scripts/verification/document_tables_http_probe.py" \
        --base-url "http://127.0.0.1:$task_app_port" --database-port "$task_pg_port" \
        --report "$DEVCORE_VOL1_F08_REPORT_DIR/document-tables-http.json"
    task_before_down=$(psql -X -h 127.0.0.1 -p "$task_pg_port" -U "$task_pg_user" -d "$task_db" -At -c 'SELECT to_jsonb(p) FROM "Posts" p WHERE "Id" = 2003;')
    if (
        cd "$task_source"
        DB_CONNECTION_STRING="Host=127.0.0.1;Port=$task_pg_port;Database=$task_db;Username=$task_pg_user" \
        dotnet ef database update 20261010065908_ContentAccess --no-build --project DevCoreBlog.csproj \
        --startup-project DevCoreBlog.csproj --context ApplicationDbContext --no-color
    ) > "$task_tmp/document-down.log" 2>&1; then
        printf 'f08_down_with_document_data_is_rejected=false\n'; exit 1
    fi
    rg -q 'Document data must be retained' "$task_tmp/document-down.log"
    task_after_down=$(psql -X -h 127.0.0.1 -p "$task_pg_port" -U "$task_pg_user" -d "$task_db" -At -c 'SELECT to_jsonb(p) FROM "Posts" p WHERE "Id" = 2003;')
    [ "$task_before_down" = "$task_after_down" ]
    printf 'f08_down_with_document_data_is_rejected=true\nf08_refused_down_preserves_document=true\n'
fi

if [ "${DEVCORE_F17_HOLD_FOR_BROWSER:-0}" = 1 ]; then
    printf 'F17 browser fixture ready at http://127.0.0.1:%s\n' "$task_app_port"
    while kill -0 "$task_app_pid" 2>/dev/null; do
        sleep 1
    done
fi
