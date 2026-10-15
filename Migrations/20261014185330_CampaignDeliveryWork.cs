using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevCoreBlog.Migrations
{
    /// <inheritdoc />
    public partial class CampaignDeliveryWork : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CampaignDispatches",
                columns: table => new
                {
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProtectedContext = table.Column<string>(type: "text", nullable: false),
                    ContextHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    SnapshotHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSubscriberId = table.Column<Guid>(type: "uuid", nullable: true),
                    StagedCount = table.Column<long>(type: "bigint", nullable: false),
                    PreparationState = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignDispatches", x => x.CampaignId);
                    table.CheckConstraint("CK_CampaignDispatches_Valid", "\"Version\">0 AND \"StagedCount\">=0 AND \"PreparationState\" IN (0,1,2)\nAND \"ContextHash\" ~ '^[A-F0-9]{64}$' AND \"SnapshotHash\" ~ '^[A-F0-9]{64}$'\nAND octet_length(\"ProtectedContext\") BETWEEN 1 AND 16384 AND \"ProtectedContext\" ~ '^[A-Za-z0-9+/=]+$'\nAND isfinite(\"CreatedUtc\") AND isfinite(\"UpdatedUtc\") AND \"UpdatedUtc\">=\"CreatedUtc\"\nAND \"ExpiresUtc\"=\"CreatedUtc\"+interval '1 day' AND isfinite(\"ExpiresUtc\")\nAND ((\"StagedCount\"=0 AND \"LastSubscriberId\" IS NULL) OR (\"StagedCount\">0 AND \"LastSubscriberId\" IS NOT NULL AND \"LastSubscriberId\">'00000000-0000-0000-0000-000000000000'::uuid))");
                    table.ForeignKey(
                        name: "FK_CampaignDispatches_NewsletterCampaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "NewsletterCampaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CampaignDeliveryJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberVersion = table.Column<long>(type: "bigint", nullable: false),
                    PeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreparationVersion = table.Column<long>(type: "bigint", nullable: false),
                    ProtectedPayload = table.Column<string>(type: "text", nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DueUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FirstAttemptUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    Problem = table.Column<int>(type: "integer", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProviderMessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignDeliveryJobs", x => x.Id);
                    table.CheckConstraint("CK_CampaignDeliveryJobs_Payload", "octet_length(\"ProtectedPayload\") BETWEEN 1 AND 4194304 AND \"ProtectedPayload\" ~ '^[A-Za-z0-9+/=]+$' AND \"PayloadHash\" ~ '^[A-F0-9]{64}$'");
                    table.CheckConstraint("CK_CampaignDeliveryJobs_State", "\"Id\"<>'00000000-0000-0000-0000-000000000000'::uuid AND \"PeriodId\"<>'00000000-0000-0000-0000-000000000000'::uuid\nAND \"SubscriberVersion\">0 AND \"PreparationVersion\">=2 AND \"Version\">0 AND \"State\" BETWEEN 0 AND 7 AND \"Problem\" BETWEEN 0 AND 10\nAND \"AttemptCount\" BETWEEN 0 AND 5 AND ((\"AttemptCount\"=0)=(\"FirstAttemptUtc\" IS NULL))\nAND ((\"State\" IN (1,2))=(\"LeaseId\" IS NOT NULL AND \"LeaseUntilUtc\" IS NOT NULL))\nAND ((\"LeaseId\" IS NULL)=(\"LeaseUntilUtc\" IS NULL)) AND (\"State\"<>2 OR \"AttemptCount\">0)\nAND ((\"State\"=3)=(\"ProviderMessageId\" IS NOT NULL))\nAND (\"ProviderMessageId\" IS NULL OR \"ProviderMessageId\"<>'00000000-0000-0000-0000-000000000000'::uuid)");
                    table.CheckConstraint("CK_CampaignDeliveryJobs_Time", "isfinite(\"CreatedUtc\") AND isfinite(\"UpdatedUtc\") AND isfinite(\"ExpiresUtc\") AND isfinite(\"DueUtc\")\nAND \"UpdatedUtc\">=\"CreatedUtc\" AND \"ExpiresUtc\">=\"CreatedUtc\"+interval '30 seconds'\nAND \"ExpiresUtc\"<=\"CreatedUtc\"+interval '1 day' AND \"DueUtc\">=\"CreatedUtc\"\nAND (\"FirstAttemptUtc\" IS NULL OR (isfinite(\"FirstAttemptUtc\") AND \"FirstAttemptUtc\" BETWEEN \"CreatedUtc\" AND \"UpdatedUtc\"))\nAND (\"LeaseUntilUtc\" IS NULL OR (isfinite(\"LeaseUntilUtc\") AND \"LeaseUntilUtc\">\"UpdatedUtc\"))");
                    table.ForeignKey(
                        name: "FK_CampaignDeliveryJobs_CampaignDispatches_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "CampaignDispatches",
                        principalColumn: "CampaignId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CampaignDeliveryJobs_NewsletterRecipients_CampaignId_Subscr~",
                        columns: x => new { x.CampaignId, x.SubscriberId },
                        principalTable: "NewsletterRecipients",
                        principalColumns: new[] { "CampaignId", "SubscriberId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CampaignDeliveryAttempts",
                columns: table => new
                {
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Outcome = table.Column<int>(type: "integer", nullable: true),
                    Reason = table.Column<int>(type: "integer", nullable: true),
                    HttpStatus = table.Column<int>(type: "integer", nullable: true),
                    ProviderMessageId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignDeliveryAttempts", x => new { x.JobId, x.Number });
                    table.CheckConstraint("CK_CampaignDeliveryAttempts_Valid", "\"Number\" BETWEEN 1 AND 5 AND \"LeaseId\"<>'00000000-0000-0000-0000-000000000000'::uuid AND isfinite(\"StartedUtc\")\nAND ((\"CompletedUtc\" IS NULL)=(\"Outcome\" IS NULL)) AND ((\"Outcome\" IS NULL)=(\"Reason\" IS NULL))\nAND (\"Outcome\" IS NULL OR \"Outcome\" BETWEEN 0 AND 4) AND (\"Reason\" IS NULL OR \"Reason\" BETWEEN 0 AND 15)\nAND (\"CompletedUtc\" IS NULL OR (isfinite(\"CompletedUtc\") AND \"CompletedUtc\">=\"StartedUtc\"))\nAND (\"HttpStatus\" IS NULL OR \"HttpStatus\" BETWEEN 100 AND 599)\nAND (\"ProviderMessageId\" IS NULL OR (\"Outcome\"=0 AND \"ProviderMessageId\"<>'00000000-0000-0000-0000-000000000000'::uuid))");
                    table.ForeignKey(
                        name: "FK_CampaignDeliveryAttempts_CampaignDeliveryJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "CampaignDeliveryJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignDeliveryJobs_CampaignId_PreparationVersion",
                table: "CampaignDeliveryJobs",
                columns: new[] { "CampaignId", "PreparationVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignDeliveryJobs_CampaignId_SubscriberId",
                table: "CampaignDeliveryJobs",
                columns: new[] { "CampaignId", "SubscriberId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CampaignDeliveryJobs_DueUtc_Id",
                table: "CampaignDeliveryJobs",
                columns: new[] { "DueUtc", "Id" },
                filter: "\"State\"=0");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignDeliveryJobs_LeaseUntilUtc",
                table: "CampaignDeliveryJobs",
                column: "LeaseUntilUtc",
                filter: "\"State\" IN (1,2)");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignDeliveryJobs_ProviderMessageId",
                table: "CampaignDeliveryJobs",
                column: "ProviderMessageId",
                unique: true,
                filter: "\"ProviderMessageId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignDispatches_CreatedUtc_CampaignId",
                table: "CampaignDispatches",
                columns: new[] { "CreatedUtc", "CampaignId" },
                filter: "\"PreparationState\"=0");
            migrationBuilder.Sql("""
                CREATE FUNCTION "GuardCampaignDispatch"() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE amount bigint; last_id uuid;
                BEGIN
                    IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Dispatch history must be retained.' USING ERRCODE='23514'; END IF;
                    IF TG_OP='INSERT' THEN
                        PERFORM 1 FROM "NewsletterCampaigns" WHERE "Id"=NEW."CampaignId" AND "State"=0 AND "SnapshotHash"=NEW."SnapshotHash" AND "CreatedUtc"<=NEW."CreatedUtc" FOR UPDATE;
                        IF NOT FOUND OR NOT EXISTS(SELECT 1 FROM "NewsletterRecipientSelections" WHERE "CampaignId"=NEW."CampaignId" AND "State"=1 AND "UpdatedUtc"<=NEW."CreatedUtc")
                            OR NEW."Version"<>1 OR NEW."StagedCount"<>0 OR NEW."LastSubscriberId" IS NOT NULL OR NEW."PreparationState"<>0 OR NEW."UpdatedUtc"<>NEW."CreatedUtc" THEN
                            RAISE EXCEPTION 'Only an explicit completed recipient intent may be armed.' USING ERRCODE='23514';
                        END IF;
                    ELSE
                        IF (to_jsonb(NEW)-ARRAY['LastSubscriberId','StagedCount','PreparationState','UpdatedUtc','Version']) IS DISTINCT FROM
                            (to_jsonb(OLD)-ARRAY['LastSubscriberId','StagedCount','PreparationState','UpdatedUtc','Version'])
                            OR OLD."PreparationState"<>0 OR NEW."Version"<>OLD."Version"+1 OR NEW."UpdatedUtc"<OLD."UpdatedUtc"
                            OR NEW."StagedCount"-OLD."StagedCount" NOT BETWEEN 0 AND 10 THEN
                            RAISE EXCEPTION 'Dispatch context is immutable; preparation is monotonic.' USING ERRCODE='23514';
                        END IF;
                        SELECT count(*) INTO amount FROM "CampaignDeliveryJobs" WHERE "CampaignId"=NEW."CampaignId" AND "PreparationVersion"=NEW."Version";
                        SELECT "SubscriberId" INTO last_id FROM "CampaignDeliveryJobs" WHERE "CampaignId"=NEW."CampaignId" AND "PreparationVersion"=NEW."Version" ORDER BY "SubscriberId" DESC LIMIT 1;
                        IF NEW."StagedCount"-OLD."StagedCount"<>amount OR
                            (amount=0 AND NEW."LastSubscriberId" IS DISTINCT FROM OLD."LastSubscriberId") OR
                            (amount>0 AND (NEW."LastSubscriberId" IS DISTINCT FROM last_id OR NEW."LastSubscriberId"<=coalesce(OLD."LastSubscriberId",'00000000-0000-0000-0000-000000000000'::uuid)))
                            OR (NEW."PreparationState"=2 AND amount<>0) THEN
                            RAISE EXCEPTION 'Preparation rows and checkpoint must advance together.' USING ERRCODE='23514';
                        END IF;
                        IF NEW."PreparationState"=1 AND NEW."StagedCount"<>(SELECT count(*) FROM "NewsletterRecipients" WHERE "CampaignId"=NEW."CampaignId") THEN
                            RAISE EXCEPTION 'Complete preparation requires every selected recipient.' USING ERRCODE='23514';
                        END IF;
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER "TR_CampaignDispatches_Guard" BEFORE INSERT OR UPDATE OR DELETE ON "CampaignDispatches" FOR EACH ROW EXECUTE FUNCTION "GuardCampaignDispatch"();

                CREATE FUNCTION "GuardCampaignDeliveryJob"() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE dispatch "CampaignDispatches"%ROWTYPE;
                BEGIN
                    IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Delivery history must be retained.' USING ERRCODE='23514'; END IF;
                    IF TG_OP='INSERT' THEN
                        SELECT * INTO dispatch FROM "CampaignDispatches" WHERE "CampaignId"=NEW."CampaignId";
                        IF NOT FOUND OR dispatch."PreparationState"<>0 OR NEW."PreparationVersion"<>dispatch."Version"+1
                            OR NEW."CreatedUtc"<dispatch."CreatedUtc" OR NEW."ExpiresUtc"<>dispatch."ExpiresUtc"
                            OR NEW."SubscriberId"<=coalesce(dispatch."LastSubscriberId",'00000000-0000-0000-0000-000000000000'::uuid)
                            OR NEW."Version"<>1 OR NEW."State"<>0 OR NEW."Problem"<>0 OR NEW."AttemptCount"<>0
                            OR NEW."UpdatedUtc"<>NEW."CreatedUtc" OR NEW."DueUtc"<>NEW."CreatedUtc"
                            OR NOT EXISTS(SELECT 1 FROM "NewsletterRecipients" WHERE "CampaignId"=NEW."CampaignId" AND "SubscriberId"=NEW."SubscriberId"
                                AND "SubscriberVersion"=NEW."SubscriberVersion" AND "PeriodId"=NEW."PeriodId") THEN
                            RAISE EXCEPTION 'Delivery must match a selected recipient and preparation page.' USING ERRCODE='23514';
                        END IF;
                    ELSE
                        IF ROW(NEW."Id",NEW."CampaignId",NEW."SubscriberId",NEW."SubscriberVersion",NEW."PeriodId",NEW."PreparationVersion",NEW."ProtectedPayload",NEW."PayloadHash",NEW."CreatedUtc",NEW."ExpiresUtc") IS DISTINCT FROM
                            ROW(OLD."Id",OLD."CampaignId",OLD."SubscriberId",OLD."SubscriberVersion",OLD."PeriodId",OLD."PreparationVersion",OLD."ProtectedPayload",OLD."PayloadHash",OLD."CreatedUtc",OLD."ExpiresUtc")
                            OR NEW."Version"<>OLD."Version"+1 OR NEW."UpdatedUtc"<OLD."UpdatedUtc" OR OLD."State">=3
                            OR (OLD."State"=0 AND NEW."State" NOT IN (1,5,6,7)) OR (OLD."State"=1 AND NEW."State" NOT IN (0,2,5,6,7))
                            OR (OLD."State"=2 AND NEW."State" NOT IN (0,3,4,5,6)) THEN
                            RAISE EXCEPTION 'Invalid delivery transition or payload mutation.' USING ERRCODE='23514';
                        END IF;
                        IF NEW."State"=2 THEN
                            IF NEW."AttemptCount"<>OLD."AttemptCount"+1 OR NEW."LeaseId" IS DISTINCT FROM OLD."LeaseId" OR OLD."LeaseUntilUtc"<=NEW."UpdatedUtc"
                                OR NEW."FirstAttemptUtc" IS DISTINCT FROM coalesce(OLD."FirstAttemptUtc",NEW."UpdatedUtc")
                                OR NEW."ExpiresUtc"<=NEW."UpdatedUtc"+interval '30 seconds' OR NEW."UpdatedUtc"-NEW."FirstAttemptUtc">=interval '23 hours'
                                OR NOT EXISTS(SELECT 1 FROM "NewsletterCampaigns" WHERE "Id"=NEW."CampaignId" AND "State"=0)
                                OR NOT EXISTS(SELECT 1 FROM "Subscribers" s JOIN "NewsletterRecipients" r ON r."CampaignId"=NEW."CampaignId" AND r."SubscriberId"=s."Id"
                                    WHERE s."Id"=NEW."SubscriberId" AND s."Version"=NEW."SubscriberVersion" AND s."PeriodId"=NEW."PeriodId" AND s."AddressKey"=r."AddressKey"
                                        AND s."Status"=1 AND s."VerifiedUtc" IS NOT NULL AND s."Suppression"=0 AND s."UpdatedUtc"<=NEW."UpdatedUtc"
                                        AND NOT EXISTS(SELECT 1 FROM "ProviderAddressSuppressions" p WHERE p."AddressKey"=s."AddressKey")) THEN
                                RAISE EXCEPTION 'Sending requires a current eligible lifecycle and lease.' USING ERRCODE='23514';
                            END IF;
                        ELSE
                            IF NEW."AttemptCount"<>OLD."AttemptCount" OR NEW."FirstAttemptUtc" IS DISTINCT FROM OLD."FirstAttemptUtc" THEN
                                RAISE EXCEPTION 'Attempt history cannot rewind.' USING ERRCODE='23514';
                            END IF;
                            IF OLD."State"=1 AND NEW."State"=0 AND OLD."LeaseUntilUtc">NEW."UpdatedUtc" THEN
                                RAISE EXCEPTION 'Only an expired unstarted lease may be reclaimed.' USING ERRCODE='23514';
                            END IF;
                        END IF;
                        IF OLD."State"<>2 AND NEW."DueUtc"<>OLD."DueUtc" THEN
                            RAISE EXCEPTION 'Only a completed attempt may schedule a new due time.' USING ERRCODE='23514';
                        END IF;
                        IF OLD."State"=2 AND NEW."State" IN (0,3,4) AND OLD."LeaseUntilUtc"<=NEW."UpdatedUtc" THEN
                            RAISE EXCEPTION 'A stale sender cannot publish a definitive result.' USING ERRCODE='23514';
                        END IF;
                        IF OLD."State"=2 AND NEW."State"=0 AND (NEW."AttemptCount">=5
                            OR NEW."DueUtc"<NEW."UpdatedUtc"+CASE NEW."AttemptCount" WHEN 1 THEN interval '30 seconds' WHEN 2 THEN interval '120 seconds' WHEN 3 THEN interval '600 seconds' ELSE interval '1800 seconds' END
                            OR NEW."DueUtc">=NEW."FirstAttemptUtc"+interval '23 hours' OR NEW."DueUtc">=NEW."ExpiresUtc"-interval '30 seconds') THEN
                            RAISE EXCEPTION 'Retry must fit the original bounded key window.' USING ERRCODE='23514';
                        END IF;
                        IF NEW."State"=1 AND (NEW."DueUtc">NEW."UpdatedUtc" OR NEW."ExpiresUtc"<=NEW."UpdatedUtc"+interval '30 seconds'
                            OR NOT EXISTS(SELECT 1 FROM "CampaignDispatches" WHERE "CampaignId"=NEW."CampaignId" AND "PreparationState"=1)
                            OR NOT EXISTS(SELECT 1 FROM "NewsletterCampaigns" WHERE "Id"=NEW."CampaignId" AND "State"=0)) THEN
                            RAISE EXCEPTION 'Claim requires complete preparation and a due live campaign.' USING ERRCODE='23514';
                        END IF;
                        IF NEW."State" IN (1,2) AND (NEW."LeaseId" IS NULL OR NEW."LeaseId"='00000000-0000-0000-0000-000000000000'::uuid OR NEW."LeaseUntilUtc"<>NEW."UpdatedUtc"+interval '2 minutes') THEN
                            RAISE EXCEPTION 'Invalid lease.' USING ERRCODE='23514';
                        END IF;
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER "TR_CampaignDeliveryJobs_Guard" BEFORE INSERT OR UPDATE OR DELETE ON "CampaignDeliveryJobs" FOR EACH ROW EXECUTE FUNCTION "GuardCampaignDeliveryJob"();

                CREATE FUNCTION "GuardCampaignAttempt"() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Attempt history must be retained.' USING ERRCODE='23514'; END IF;
                    IF TG_OP='INSERT' THEN
                        IF NEW."CompletedUtc" IS NOT NULL OR NEW."Outcome" IS NOT NULL OR NEW."Reason" IS NOT NULL OR NEW."HttpStatus" IS NOT NULL OR NEW."ProviderMessageId" IS NOT NULL
                            OR NOT EXISTS(SELECT 1 FROM "CampaignDeliveryJobs" WHERE "Id"=NEW."JobId" AND "State"=2 AND "AttemptCount"=NEW."Number" AND "LeaseId"=NEW."LeaseId" AND "UpdatedUtc"=NEW."StartedUtc") THEN
                            RAISE EXCEPTION 'Attempt must accompany a durable start.' USING ERRCODE='23514';
                        END IF;
                    ELSE
                        IF OLD."CompletedUtc" IS NOT NULL OR NEW."CompletedUtc" IS NULL
                            OR ROW(NEW."JobId",NEW."Number",NEW."LeaseId",NEW."StartedUtc") IS DISTINCT FROM ROW(OLD."JobId",OLD."Number",OLD."LeaseId",OLD."StartedUtc")
                            OR NOT EXISTS(SELECT 1 FROM "CampaignDeliveryJobs" WHERE "Id"=NEW."JobId" AND "State"=2 AND "AttemptCount"=NEW."Number" AND "LeaseId"=NEW."LeaseId") THEN
                            RAISE EXCEPTION 'Only the unfinished current attempt may be completed once.' USING ERRCODE='23514';
                        END IF;
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER "TR_CampaignDeliveryAttempts_Guard" BEFORE INSERT OR UPDATE OR DELETE ON "CampaignDeliveryAttempts" FOR EACH ROW EXECUTE FUNCTION "GuardCampaignAttempt"();

                CREATE FUNCTION "RequireCampaignWorkCommit"() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE id uuid; campaign uuid; preparation bigint; state integer; attempts integer; lease uuid; provider uuid; first_time timestamptz; result "CampaignDeliveryAttempts"%ROWTYPE;
                BEGIN
                    IF TG_TABLE_NAME='CampaignDeliveryJobs' THEN id=NEW."Id"; ELSE id=NEW."JobId"; END IF;
                    SELECT "CampaignId","PreparationVersion","State","AttemptCount","LeaseId","ProviderMessageId","FirstAttemptUtc"
                        INTO campaign,preparation,state,attempts,lease,provider,first_time FROM "CampaignDeliveryJobs" WHERE "Id"=id;
                    IF NOT EXISTS(SELECT 1 FROM "CampaignDispatches" WHERE "CampaignId"=campaign AND "Version">=preparation) THEN
                        RAISE EXCEPTION 'Job and preparation checkpoint must commit together.' USING ERRCODE='23514';
                    END IF;
                    IF attempts<>(SELECT count(*) FROM "CampaignDeliveryAttempts" WHERE "JobId"=id) THEN
                        RAISE EXCEPTION 'Job counter and attempts must commit together.' USING ERRCODE='23514';
                    END IF;
                    IF attempts>0 THEN
                        SELECT * INTO result FROM "CampaignDeliveryAttempts" WHERE "JobId"=id AND "Number"=attempts;
                        IF NOT FOUND OR first_time IS DISTINCT FROM (SELECT "StartedUtc" FROM "CampaignDeliveryAttempts" WHERE "JobId"=id AND "Number"=1)
                            OR (state=2 AND (result."CompletedUtc" IS NOT NULL OR result."LeaseId" IS DISTINCT FROM lease))
                            OR (state<>2 AND result."CompletedUtc" IS NULL)
                            OR (state=3 AND (result."Outcome"<>0 OR result."ProviderMessageId" IS DISTINCT FROM provider))
                            OR (state=4 AND result."Outcome"<>1) OR (state=0 AND result."Outcome"<>2) THEN
                            RAISE EXCEPTION 'Attempt result does not match durable delivery state.' USING ERRCODE='23514';
                        END IF;
                    END IF;
                    RETURN NULL;
                END $$;
                CREATE CONSTRAINT TRIGGER "TR_CampaignDeliveryJobs_Commit" AFTER INSERT OR UPDATE ON "CampaignDeliveryJobs" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION "RequireCampaignWorkCommit"();
                CREATE CONSTRAINT TRIGGER "TR_CampaignDeliveryAttempts_Commit" AFTER INSERT OR UPDATE ON "CampaignDeliveryAttempts" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION "RequireCampaignWorkCommit"();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE "CampaignDispatches","CampaignDeliveryJobs","CampaignDeliveryAttempts" IN ACCESS EXCLUSIVE MODE;
                DO $$ BEGIN IF EXISTS(SELECT 1 FROM "CampaignDispatches") OR EXISTS(SELECT 1 FROM "CampaignDeliveryJobs") OR EXISTS(SELECT 1 FROM "CampaignDeliveryAttempts") THEN
                    RAISE EXCEPTION 'Campaign dispatch and attempt history must be retained; use forward repair.';
                END IF; END $$;
                """);
            migrationBuilder.DropTable(
                name: "CampaignDeliveryAttempts");

            migrationBuilder.DropTable(
                name: "CampaignDeliveryJobs");

            migrationBuilder.DropTable(
                name: "CampaignDispatches");
            migrationBuilder.Sql("DROP FUNCTION \"RequireCampaignWorkCommit\"(); DROP FUNCTION \"GuardCampaignAttempt\"(); DROP FUNCTION \"GuardCampaignDeliveryJob\"(); DROP FUNCTION \"GuardCampaignDispatch\"();");
        }
    }
}
