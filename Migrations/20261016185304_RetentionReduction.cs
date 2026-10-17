using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevCoreBlog.Migrations
{
    /// <inheritdoc />
    public partial class RetentionReduction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TransactionalEmailJobs_Payload",
                table: "TransactionalEmailJobs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SubscriberVerificationTokens_Valid",
                table: "SubscriberVerificationTokens");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ReaderLoginTokens_Valid",
                table: "ReaderLoginTokens");

            migrationBuilder.AlterColumn<string>(
                name: "ProtectedPayload",
                table: "TransactionalEmailJobs",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<DateTime>(
                name: "PayloadRedactedUtc",
                table: "TransactionalEmailJobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TerminalUtc",
                table: "TransactionalEmailJobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Digest",
                table: "SubscriberVerificationTokens",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                collation: "C",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldCollation: "C");

            migrationBuilder.AddColumn<DateTime>(
                name: "RedactedUtc",
                table: "SubscriberVerificationTokens",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Digest",
                table: "ReaderLoginTokens",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AddColumn<DateTime>(
                name: "RedactedUtc",
                table: "ReaderLoginTokens",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProviderEmailReceipts",
                columns: table => new
                {
                    EventId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    ReceivedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderEmailReceipts", x => x.EventId);
                    table.CheckConstraint("CK_ProviderEmailReceipt", "\"EventId\" ~ '^[A-Za-z0-9_-]{1,128}$' AND \"PayloadHash\" ~ '^[A-Fa-f0-9]{64}$' AND isfinite(\"ReceivedUtc\")");
                });

            migrationBuilder.CreateTable(
                name: "RetentionCheckpoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    LastAppliedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetentionCheckpoints", x => x.Id);
                    table.CheckConstraint("CK_RetentionCheckpoint", "\"Id\"=1 AND isfinite(\"LastAppliedUtc\")");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_TransactionalEmailJobs_Payload",
                table: "TransactionalEmailJobs",
                sql: "\"PayloadHash\" ~ '^[A-F0-9]{64}$' AND\n((\"PayloadRedactedUtc\" IS NULL AND \"ProtectedPayload\" IS NOT NULL\n    AND octet_length(\"ProtectedPayload\") BETWEEN 1 AND 4194304 AND \"ProtectedPayload\" ~ '^[A-Za-z0-9+/=]+$')\nOR (\"PayloadRedactedUtc\" IS NOT NULL AND isfinite(\"PayloadRedactedUtc\") AND \"ProtectedPayload\" IS NULL\n    AND \"State\" IN (3,4,6,7) AND \"TerminalUtc\" IS NOT NULL\n    AND \"PayloadRedactedUtc\" > greatest(\"ExpiresUtc\",\"TerminalUtc\") + interval '86400 seconds'))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TransactionalEmailJobs_Terminal",
                table: "TransactionalEmailJobs",
                sql: "\"TerminalUtc\" IS NULL OR (isfinite(\"TerminalUtc\") AND \"State\" IN (3,4,6,7) AND \"TerminalUtc\">=\"CreatedUtc\")");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SubscriberVerificationTokens_Valid",
                table: "SubscriberVerificationTokens",
                sql: "\"Purpose\" = 1 AND \"SubscriberVersion\" > 0\nAND ((\"RedactedUtc\" IS NULL AND \"Digest\" IS NOT NULL AND \"Digest\" ~ '^[A-F0-9]{64}$')\n    OR (\"RedactedUtc\" IS NOT NULL AND isfinite(\"RedactedUtc\") AND \"Digest\" IS NULL\n        AND \"RedactedUtc\">greatest(\"ExpiresUtc\",\"ConsumedUtc\") + interval '604800 seconds'))\nAND \"AddressBinding\" ~ '^[A-F0-9]{64}$'\nAND isfinite(\"CreatedUtc\") AND isfinite(\"ExpiresUtc\") AND \"ExpiresUtc\" = \"CreatedUtc\" + interval '86400 seconds'\nAND (\"ConsumedUtc\" IS NULL OR (isfinite(\"ConsumedUtc\") AND \"ConsumedUtc\" >= \"CreatedUtc\" AND \"ConsumedUtc\" < \"ExpiresUtc\"))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ReaderLoginTokens_Valid",
                table: "ReaderLoginTokens",
                sql: "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"SubscriberVersion\" > 0\nAND \"PeriodId\" <> '00000000-0000-0000-0000-000000000000'::uuid\nAND ((\"RedactedUtc\" IS NULL AND \"Digest\" IS NOT NULL AND \"Digest\" ~ '^[A-F0-9]{64}$')\n    OR (\"RedactedUtc\" IS NOT NULL AND isfinite(\"RedactedUtc\") AND \"Digest\" IS NULL\n        AND \"RedactedUtc\">greatest(\"ExpiresUtc\",\"ConsumedUtc\") + interval '604800 seconds'))\nAND \"AddressBinding\" ~ '^[A-F0-9]{64}$'\nAND isfinite(\"CreatedUtc\") AND isfinite(\"ExpiresUtc\") AND \"ExpiresUtc\" = \"CreatedUtc\" + interval '15 minutes'\nAND (\"ConsumedUtc\" IS NULL OR (isfinite(\"ConsumedUtc\") AND \"ConsumedUtc\" >= \"CreatedUtc\" AND \"ConsumedUtc\" < \"ExpiresUtc\"))");
            migrationBuilder.Sql("""
                INSERT INTO "RetentionCheckpoints" ("Id","LastAppliedUtc") VALUES (1,'1900-01-01Z');
                INSERT INTO "ProviderEmailReceipts" ("EventId","PayloadHash","ReceivedUtc") SELECT "EventId","PayloadHash","ReceivedUtc" FROM "ProviderEmailEvents";
                UPDATE "TransactionalEmailJobs" j SET "TerminalUtc"=greatest(j."UpdatedUtc",(SELECT max(a."CompletedUtc") FROM "TransactionalEmailAttempts" a WHERE a."JobId"=j."Id")) WHERE j."State" IN (3,4,6,7);
                CREATE FUNCTION "GuardRetentionCheckpoint"() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                 IF TG_OP='DELETE' OR NEW."Id"<>OLD."Id" OR NEW."LastAppliedUtc"<OLD."LastAppliedUtc" THEN RAISE EXCEPTION 'Retention clock cannot be removed or rewound.' USING ERRCODE='23514'; END IF;
                 RETURN NEW;
                END $$;
                CREATE TRIGGER "TR_RetentionClock" BEFORE UPDATE OR DELETE ON "RetentionCheckpoints" FOR EACH ROW EXECUTE FUNCTION "GuardRetentionCheckpoint"();
                CREATE FUNCTION "RetentionUtc"() RETURNS timestamptz LANGUAGE plpgsql STABLE AS $$
                DECLARE n timestamptz;
                BEGIN
                 n=nullif(current_setting('aether.retention_utc',true),'')::timestamptz;
                 IF n IS NULL OR NOT isfinite(n) OR NOT EXISTS(SELECT 1 FROM "RetentionCheckpoints" WHERE "Id"=1 AND "LastAppliedUtc"=n) THEN
                 RAISE EXCEPTION 'A current retention checkpoint is required.' USING ERRCODE='23514'; END IF;
                 RETURN n;
                END $$;
                CREATE FUNCTION "GuardTransactionalRetention"() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE n timestamptz;
                BEGIN
                 IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Mail replay evidence must be retained.' USING ERRCODE='23514'; END IF;
                 IF TG_OP='INSERT' THEN
                  IF NEW."PayloadRedactedUtc" IS NOT NULL OR NEW."ProtectedPayload" IS NULL THEN RAISE EXCEPTION 'New mail cannot be redacted.' USING ERRCODE='23514'; END IF;
                  NEW."TerminalUtc"=CASE WHEN NEW."State" IN (3,4,6,7) THEN NEW."UpdatedUtc" ELSE NULL END;
                  RETURN NEW;
                 END IF;
                 IF OLD."PayloadRedactedUtc" IS NULL AND NEW."PayloadRedactedUtc" IS NOT NULL THEN
                  n="RetentionUtc"();
                  IF OLD."State" NOT IN (3,4,6,7) OR OLD."LeaseId" IS NOT NULL OR OLD."TerminalUtc" IS NULL
                   OR n<=greatest(OLD."ExpiresUtc",OLD."TerminalUtc")+interval '86400 seconds' OR NEW."PayloadRedactedUtc"<>n OR NEW."ProtectedPayload" IS NOT NULL
                   OR (to_jsonb(NEW)-ARRAY['ProtectedPayload','PayloadRedactedUtc']) IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['ProtectedPayload','PayloadRedactedUtc'])
                   OR (SELECT count(*) FROM "TransactionalEmailAttempts" WHERE "JobId"=OLD."Id")<>OLD."AttemptCount"
                   OR EXISTS(SELECT 1 FROM "TransactionalEmailAttempts" WHERE "JobId"=OLD."Id" AND ("CompletedUtc" IS NULL OR "CompletedUtc">n OR "Outcome"=3)) THEN
                   RAISE EXCEPTION 'Only a closed envelope may be redacted.' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                 END IF;
                 IF OLD."PayloadRedactedUtc" IS NOT NULL OR OLD."State" IN (3,4,6,7) THEN
                  IF NEW IS DISTINCT FROM OLD THEN RAISE EXCEPTION 'Closed mail cannot be revived.' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                 END IF;
                 IF ROW(NEW."Id",NEW."Purpose",NEW."SubscriberId",NEW."SubscriberVersion",NEW."ProtectedPayload",NEW."PayloadHash",NEW."CreatedUtc",NEW."ExpiresUtc",NEW."PayloadRedactedUtc",NEW."TerminalUtc") IS DISTINCT FROM
                 ROW(OLD."Id",OLD."Purpose",OLD."SubscriberId",OLD."SubscriberVersion",OLD."ProtectedPayload",OLD."PayloadHash",OLD."CreatedUtc",OLD."ExpiresUtc",OLD."PayloadRedactedUtc",OLD."TerminalUtc") THEN
                 RAISE EXCEPTION 'Mail envelope binding is immutable.' USING ERRCODE='23514'; END IF;
                 NEW."TerminalUtc"=CASE WHEN NEW."State" IN (3,4,6,7) THEN NEW."UpdatedUtc" ELSE NULL END;
                 RETURN NEW;
                END $$;
                CREATE TRIGGER "TR_TransactionalRetention" BEFORE INSERT OR UPDATE OR DELETE ON "TransactionalEmailJobs" FOR EACH ROW EXECUTE FUNCTION "GuardTransactionalRetention"();
                CREATE FUNCTION "GuardTokenRetention"() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE n timestamptz;
                BEGIN
                 IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Minimal token admission and verification evidence must remain.' USING ERRCODE='23514'; END IF;
                 IF TG_OP='INSERT' THEN
                  IF NEW."RedactedUtc" IS NOT NULL OR NEW."Digest" IS NULL THEN RAISE EXCEPTION 'New proof cannot be redacted.' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                 END IF;
                 IF OLD."RedactedUtc" IS NULL AND NEW."RedactedUtc" IS NOT NULL THEN
                  n="RetentionUtc"();
                  IF NEW."RedactedUtc"<>n OR n<=greatest(OLD."ExpiresUtc",OLD."ConsumedUtc")+interval '604800 seconds' OR NEW."Digest" IS NOT NULL
                   OR (to_jsonb(NEW)-ARRAY['Digest','RedactedUtc']) IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['Digest','RedactedUtc']) THEN
                   RAISE EXCEPTION 'Only an old proof digest may be removed.' USING ERRCODE='23514'; END IF;
                 ELSIF OLD."RedactedUtc" IS NOT NULL OR OLD."ConsumedUtc" IS NOT NULL THEN
                  IF NEW IS DISTINCT FROM OLD THEN RAISE EXCEPTION 'Used or redacted proof cannot be revived.' USING ERRCODE='23514'; END IF;
                 ELSE
                  IF (to_jsonb(NEW)-'ConsumedUtc') IS DISTINCT FROM (to_jsonb(OLD)-'ConsumedUtc') THEN RAISE EXCEPTION 'Proof binding is immutable.' USING ERRCODE='23514'; END IF;
                 END IF;
                 RETURN NEW;
                END $$;
                CREATE TRIGGER "TR_VerificationRetention" BEFORE INSERT OR UPDATE OR DELETE ON "SubscriberVerificationTokens" FOR EACH ROW EXECUTE FUNCTION "GuardTokenRetention"();
                CREATE TRIGGER "TR_LoginRetention" BEFORE INSERT OR UPDATE OR DELETE ON "ReaderLoginTokens" FOR EACH ROW EXECUTE FUNCTION "GuardTokenRetention"();
                CREATE FUNCTION "GuardReceiptRetention"() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Provider replay tombstone is immutable.' USING ERRCODE='23514'; END $$;
                CREATE TRIGGER "TR_ProviderReceiptImmutable" BEFORE UPDATE OR DELETE ON "ProviderEmailReceipts" FOR EACH ROW EXECUTE FUNCTION "GuardReceiptRetention"();
                CREATE FUNCTION "EnsureProviderReceipt"() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                 INSERT INTO "ProviderEmailReceipts" ("EventId","PayloadHash","ReceivedUtc") VALUES (NEW."EventId",NEW."PayloadHash",NEW."ReceivedUtc") ON CONFLICT ("EventId") DO NOTHING;
                 IF NOT EXISTS(SELECT 1 FROM "ProviderEmailReceipts" WHERE "EventId"=NEW."EventId" AND "PayloadHash"=NEW."PayloadHash" AND "ReceivedUtc"=NEW."ReceivedUtc")
                 OR NEW."ReceivedUtc"+interval '7776000 seconds'<(SELECT "LastAppliedUtc" FROM "RetentionCheckpoints" WHERE "Id"=1) THEN
                 RAISE EXCEPTION 'A receipt cannot be rebound or old detail revived.' USING ERRCODE='23514'; END IF;
                 RETURN NEW;
                END $$;
                CREATE TRIGGER "TR_ProviderEventReceipt" BEFORE INSERT ON "ProviderEmailEvents" FOR EACH ROW EXECUTE FUNCTION "EnsureProviderReceipt"();
                CREATE FUNCTION "RequireReceiptDetailCommit"() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                 IF NOT EXISTS(SELECT 1 FROM "ProviderEmailEvents" WHERE "EventId"=NEW."EventId" AND "PayloadHash"=NEW."PayloadHash" AND "ReceivedUtc"=NEW."ReceivedUtc") THEN
                 RAISE EXCEPTION 'A new receipt requires atomic verified facts.' USING ERRCODE='23514'; END IF;
                 RETURN NULL;
                END $$;
                CREATE CONSTRAINT TRIGGER "TR_ReceiptDetailCommit" AFTER INSERT ON "ProviderEmailReceipts" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION "RequireReceiptDetailCommit"();
                CREATE OR REPLACE FUNCTION "GuardProviderEventHistory"() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE n timestamptz; flags integer;
                BEGIN
                 IF TG_OP<>'DELETE' THEN RAISE EXCEPTION 'Provider detail is immutable.' USING ERRCODE='23514'; END IF;
                 n="RetentionUtc"(); flags=CASE OLD."Kind" WHEN 1 THEN 1 WHEN 2 THEN 2 ELSE 0 END;
                 IF n<=OLD."ReceivedUtc"+interval '7776000 seconds'
                 OR NOT EXISTS(SELECT 1 FROM "ProviderEmailReceipts" WHERE "EventId"=OLD."EventId" AND "PayloadHash"=OLD."PayloadHash" AND "ReceivedUtc"=OLD."ReceivedUtc")
                 OR NOT EXISTS(SELECT 1 FROM "ProviderEmailObservations" WHERE "MessageId"=OLD."MessageId" AND "AddressKey"=OLD."AddressKey"
                  AND CASE OLD."Kind" WHEN 0 THEN "DeliveredUtc" WHEN 1 THEN "BouncedUtc" WHEN 2 THEN "ComplainedUtc" ELSE "TemporaryBounceUtc" END <=OLD."OccurredUtc")
                 OR (flags<>0 AND (NOT EXISTS(SELECT 1 FROM "ProviderAddressSuppressions" WHERE "AddressKey"=OLD."AddressKey" AND ("Flags"&flags)=flags)
                  OR EXISTS(SELECT 1 FROM "Subscribers" WHERE "AddressKey"=OLD."AddressKey" AND ("Suppression"&flags)<>flags))) THEN
                 RAISE EXCEPTION 'Provider receipt, observation and suppression must survive reduction.' USING ERRCODE='23514'; END IF;
                 RETURN OLD;
                END $$;
                CREATE OR REPLACE FUNCTION "GuardProviderObservation"() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE previous timestamptz[]; incoming timestamptz[]; k integer;
                BEGIN
                 IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Provider observations must be retained.' USING ERRCODE='23514'; END IF;
                 previous=ARRAY[NULL::timestamptz,NULL,NULL,NULL];
                 IF TG_OP='UPDATE' THEN
                  IF NEW."MessageId"<>OLD."MessageId" OR NEW."AddressKey"<>OLD."AddressKey" THEN RAISE EXCEPTION 'Observation cannot be rebound.' USING ERRCODE='23514'; END IF;
                  previous=ARRAY[OLD."DeliveredUtc",OLD."BouncedUtc",OLD."ComplainedUtc",OLD."TemporaryBounceUtc"];
                 END IF;
                 incoming=ARRAY[NEW."DeliveredUtc",NEW."BouncedUtc",NEW."ComplainedUtc",NEW."TemporaryBounceUtc"];
                 FOR k IN 1..4 LOOP
                  IF previous[k] IS NOT NULL AND (incoming[k] IS NULL OR incoming[k]>previous[k]) THEN RAISE EXCEPTION 'Observed facts cannot be cleared.' USING ERRCODE='23514'; END IF;
                  IF incoming[k] IS NOT NULL AND incoming[k] IS DISTINCT FROM previous[k] AND NOT EXISTS(SELECT 1 FROM "ProviderEmailEvents"
                   WHERE "MessageId"=NEW."MessageId" AND "AddressKey"=NEW."AddressKey" AND "Kind"=k-1 AND "OccurredUtc"=incoming[k]) THEN
                   RAISE EXCEPTION 'New or earlier facts require verified detail.' USING ERRCODE='23514'; END IF;
                 END LOOP;
                 RETURN NEW;
                END $$;
                CREATE FUNCTION "GuardPendingRetention"() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE n timestamptz;
                BEGIN
                 n="RetentionUtc"();
                 IF OLD."Status"<>0 OR OLD."Suppression"<>0 OR OLD."Version"<>1 OR OLD."VerifiedUtc" IS NOT NULL OR OLD."UnsubscribedUtc" IS NOT NULL
                 OR greatest(OLD."CreatedUtc",OLD."UpdatedUtc")+interval '604800 seconds'>=n
                 OR EXISTS(SELECT 1 FROM "ProviderAddressSuppressions" WHERE "AddressKey"=OLD."AddressKey")
                 OR EXISTS(SELECT 1 FROM "TransactionalEmailJobs" WHERE "SubscriberId"=OLD."Id")
                 OR EXISTS(SELECT 1 FROM "SubscriberVerificationTokens" WHERE "SubscriberId"=OLD."Id")
                 OR EXISTS(SELECT 1 FROM "ReaderLoginTokens" WHERE "SubscriberId"=OLD."Id")
                 OR EXISTS(SELECT 1 FROM "ReaderSessions" WHERE "SubscriberId"=OLD."Id")
                 OR EXISTS(SELECT 1 FROM "NewsletterRecipients" WHERE "SubscriberId"=OLD."Id") THEN
                 RAISE EXCEPTION 'Only never-used orphan pending identities may be removed.' USING ERRCODE='23514'; END IF;
                 RETURN OLD;
                END $$;
                CREATE TRIGGER "TR_PendingRetention" BEFORE DELETE ON "Subscribers" FOR EACH ROW EXECUTE FUNCTION "GuardPendingRetention"();
                CREATE FUNCTION "GuardSessionRetention"() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                 IF least(OLD."ExpiresUtc",OLD."RevokedUtc")+interval '604800 seconds'>="RetentionUtc"() THEN
                 RAISE EXCEPTION 'An active or recent session must remain.' USING ERRCODE='23514'; END IF;
                 RETURN OLD;
                END $$;
                CREATE TRIGGER "TR_SessionRetention" BEFORE DELETE ON "ReaderSessions" FOR EACH ROW EXECUTE FUNCTION "GuardSessionRetention"();
                CREATE INDEX "IX_RetentionVerification" ON "SubscriberVerificationTokens" ("ExpiresUtc","Id") WHERE "Digest" IS NOT NULL;
                CREATE INDEX "IX_RetentionLogin" ON "ReaderLoginTokens" ("ExpiresUtc","Id") WHERE "Digest" IS NOT NULL;
                CREATE INDEX "IX_RetentionPayload" ON "TransactionalEmailJobs" ("ExpiresUtc","Id") WHERE "State" IN (3,4,6,7) AND "PayloadRedactedUtc" IS NULL;
                CREATE INDEX "IX_RetentionProvider" ON "ProviderEmailEvents" ("ReceivedUtc","EventId");
                CREATE INDEX "IX_RetentionSession" ON "ReaderSessions" (least("ExpiresUtc","RevokedUtc"),"Id");
                CREATE INDEX "IX_RetentionPending" ON "Subscribers" ("CreatedUtc","Id") WHERE "Status"=0;
                CREATE INDEX "IX_RetentionHeld" ON "TransactionalEmailJobs" ("UpdatedUtc","Id") WHERE "State"=5;
                CREATE FUNCTION "GuardTransactionalAttemptRetention"() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                 IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Mail attempt evidence must remain.' USING ERRCODE='23514'; END IF;
                 IF ROW(NEW."JobId",NEW."Number",NEW."LeaseId",NEW."StartedUtc") IS DISTINCT FROM ROW(OLD."JobId",OLD."Number",OLD."LeaseId",OLD."StartedUtc")
                 OR (OLD."CompletedUtc" IS NOT NULL AND NEW IS DISTINCT FROM OLD) THEN RAISE EXCEPTION 'Attempt evidence is immutable.' USING ERRCODE='23514'; END IF;
                 RETURN NEW;
                END $$;
                CREATE TRIGGER "TR_TransactionalAttemptRetention" BEFORE UPDATE OR DELETE ON "TransactionalEmailAttempts" FOR EACH ROW EXECUTE FUNCTION "GuardTransactionalAttemptRetention"();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE "ProviderEmailEvents","ProviderEmailReceipts","TransactionalEmailJobs","SubscriberVerificationTokens","ReaderLoginTokens","ReaderSessions","Subscribers","RetentionCheckpoints" IN ACCESS EXCLUSIVE MODE;
                DO $$ BEGIN
                 IF EXISTS(SELECT 1 FROM "Subscribers") OR EXISTS(SELECT 1 FROM "ProviderEmailReceipts") OR EXISTS(SELECT 1 FROM "TransactionalEmailJobs")
                 OR EXISTS(SELECT 1 FROM "SubscriberVerificationTokens") OR EXISTS(SELECT 1 FROM "ReaderLoginTokens") OR EXISTS(SELECT 1 FROM "ReaderSessions")
                 OR EXISTS(SELECT 1 FROM "SubscriberVerificationTokens" WHERE "RedactedUtc" IS NOT NULL) OR EXISTS(SELECT 1 FROM "ReaderLoginTokens" WHERE "RedactedUtc" IS NOT NULL)
                 OR EXISTS(SELECT 1 FROM "RetentionCheckpoints" WHERE "LastAppliedUtc">'1900-01-01Z') THEN
                 RAISE EXCEPTION 'Retention evidence and irreversible reductions require forward repair.'; END IF;
                END $$;
                DROP TRIGGER "TR_TransactionalRetention" ON "TransactionalEmailJobs";
                DROP TRIGGER "TR_TransactionalAttemptRetention" ON "TransactionalEmailAttempts";
                DROP TRIGGER "TR_VerificationRetention" ON "SubscriberVerificationTokens";
                DROP TRIGGER "TR_LoginRetention" ON "ReaderLoginTokens";
                DROP TRIGGER "TR_SessionRetention" ON "ReaderSessions";
                DROP TRIGGER "TR_PendingRetention" ON "Subscribers";
                DROP TRIGGER "TR_ProviderReceiptImmutable" ON "ProviderEmailReceipts";
                DROP TRIGGER "TR_ProviderEventReceipt" ON "ProviderEmailEvents";
                DROP TRIGGER "TR_ReceiptDetailCommit" ON "ProviderEmailReceipts";
                DROP TRIGGER "TR_RetentionClock" ON "RetentionCheckpoints";
                DROP FUNCTION "GuardTransactionalRetention"();
                DROP FUNCTION "GuardTransactionalAttemptRetention"();
                DROP FUNCTION "GuardTokenRetention"();
                DROP FUNCTION "GuardSessionRetention"();
                DROP FUNCTION "GuardPendingRetention"();
                DROP FUNCTION "GuardReceiptRetention"();
                DROP FUNCTION "EnsureProviderReceipt"();
                DROP FUNCTION "RequireReceiptDetailCommit"();
                DROP FUNCTION "GuardRetentionCheckpoint"();
                DROP FUNCTION "RetentionUtc"();
                DROP INDEX "IX_RetentionVerification";
                DROP INDEX "IX_RetentionLogin";
                DROP INDEX "IX_RetentionPayload";
                DROP INDEX "IX_RetentionProvider";
                DROP INDEX "IX_RetentionSession";
                DROP INDEX "IX_RetentionPending";
                DROP INDEX "IX_RetentionHeld";
                CREATE OR REPLACE FUNCTION "GuardProviderEventHistory"() RETURNS trigger LANGUAGE plpgsql AS $$
                                BEGIN RAISE EXCEPTION 'Provider receipt history is immutable.' USING ERRCODE='23514'; END $$;
                CREATE OR REPLACE FUNCTION "GuardProviderObservation"() RETURNS trigger LANGUAGE plpgsql AS $$
                                BEGIN
                                    IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Provider observations must be retained.' USING ERRCODE='23514'; END IF;
                                    IF TG_OP='UPDATE' AND (NEW."MessageId"<>OLD."MessageId" OR NEW."AddressKey"<>OLD."AddressKey"
                                        OR (OLD."DeliveredUtc" IS NOT NULL AND (NEW."DeliveredUtc" IS NULL OR NEW."DeliveredUtc">OLD."DeliveredUtc"))
                                        OR (OLD."BouncedUtc" IS NOT NULL AND (NEW."BouncedUtc" IS NULL OR NEW."BouncedUtc">OLD."BouncedUtc"))
                                        OR (OLD."ComplainedUtc" IS NOT NULL AND (NEW."ComplainedUtc" IS NULL OR NEW."ComplainedUtc">OLD."ComplainedUtc"))
                                        OR (OLD."TemporaryBounceUtc" IS NOT NULL AND (NEW."TemporaryBounceUtc" IS NULL OR NEW."TemporaryBounceUtc">OLD."TemporaryBounceUtc"))) THEN
                                        RAISE EXCEPTION 'Observed facts cannot be cleared or rebound.' USING ERRCODE='23514';
                                    END IF;
                                    IF (NEW."DeliveredUtc" IS NOT NULL AND NOT EXISTS(SELECT 1 FROM "ProviderEmailEvents" WHERE "MessageId"=NEW."MessageId" AND "AddressKey"=NEW."AddressKey" AND "Kind"=0 AND "OccurredUtc"=NEW."DeliveredUtc"))
                                        OR (NEW."BouncedUtc" IS NOT NULL AND NOT EXISTS(SELECT 1 FROM "ProviderEmailEvents" WHERE "MessageId"=NEW."MessageId" AND "AddressKey"=NEW."AddressKey" AND "Kind"=1 AND "OccurredUtc"=NEW."BouncedUtc"))
                                        OR (NEW."ComplainedUtc" IS NOT NULL AND NOT EXISTS(SELECT 1 FROM "ProviderEmailEvents" WHERE "MessageId"=NEW."MessageId" AND "AddressKey"=NEW."AddressKey" AND "Kind"=2 AND "OccurredUtc"=NEW."ComplainedUtc"))
                                        OR (NEW."TemporaryBounceUtc" IS NOT NULL AND NOT EXISTS(SELECT 1 FROM "ProviderEmailEvents" WHERE "MessageId"=NEW."MessageId" AND "AddressKey"=NEW."AddressKey" AND "Kind"=3 AND "OccurredUtc"=NEW."TemporaryBounceUtc")) THEN
                                        RAISE EXCEPTION 'Observation requires a corresponding receipt.' USING ERRCODE='23514';
                                    END IF;
                                    RETURN NEW;
                                END $$;
                """);
            migrationBuilder.DropTable(
                name: "ProviderEmailReceipts");

            migrationBuilder.DropTable(
                name: "RetentionCheckpoints");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TransactionalEmailJobs_Payload",
                table: "TransactionalEmailJobs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TransactionalEmailJobs_Terminal",
                table: "TransactionalEmailJobs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SubscriberVerificationTokens_Valid",
                table: "SubscriberVerificationTokens");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ReaderLoginTokens_Valid",
                table: "ReaderLoginTokens");

            migrationBuilder.DropColumn(
                name: "PayloadRedactedUtc",
                table: "TransactionalEmailJobs");

            migrationBuilder.DropColumn(
                name: "TerminalUtc",
                table: "TransactionalEmailJobs");

            migrationBuilder.DropColumn(
                name: "RedactedUtc",
                table: "SubscriberVerificationTokens");

            migrationBuilder.DropColumn(
                name: "RedactedUtc",
                table: "ReaderLoginTokens");

            migrationBuilder.AlterColumn<string>(
                name: "ProtectedPayload",
                table: "TransactionalEmailJobs",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Digest",
                table: "SubscriberVerificationTokens",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                collation: "C",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true,
                oldCollation: "C");

            migrationBuilder.AlterColumn<string>(
                name: "Digest",
                table: "ReaderLoginTokens",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_TransactionalEmailJobs_Payload",
                table: "TransactionalEmailJobs",
                sql: "octet_length(\"ProtectedPayload\") BETWEEN 1 AND 4194304\nAND \"ProtectedPayload\" ~ '^[A-Za-z0-9+/=]+$' AND \"PayloadHash\" ~ '^[A-F0-9]{64}$'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SubscriberVerificationTokens_Valid",
                table: "SubscriberVerificationTokens",
                sql: "\"Purpose\" = 1 AND \"SubscriberVersion\" > 0\nAND \"Digest\" ~ '^[A-F0-9]{64}$' AND \"AddressBinding\" ~ '^[A-F0-9]{64}$'\nAND isfinite(\"CreatedUtc\") AND isfinite(\"ExpiresUtc\") AND \"ExpiresUtc\" = \"CreatedUtc\" + interval '86400 seconds'\nAND (\"ConsumedUtc\" IS NULL OR (isfinite(\"ConsumedUtc\") AND \"ConsumedUtc\" >= \"CreatedUtc\" AND \"ConsumedUtc\" < \"ExpiresUtc\"))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ReaderLoginTokens_Valid",
                table: "ReaderLoginTokens",
                sql: "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"SubscriberVersion\" > 0\nAND \"PeriodId\" <> '00000000-0000-0000-0000-000000000000'::uuid\nAND \"Digest\" ~ '^[A-F0-9]{64}$' AND \"AddressBinding\" ~ '^[A-F0-9]{64}$'\nAND isfinite(\"CreatedUtc\") AND isfinite(\"ExpiresUtc\") AND \"ExpiresUtc\" = \"CreatedUtc\" + interval '15 minutes'\nAND (\"ConsumedUtc\" IS NULL OR (isfinite(\"ConsumedUtc\") AND \"ConsumedUtc\" >= \"CreatedUtc\" AND \"ConsumedUtc\" < \"ExpiresUtc\"))");
        }
    }
}
