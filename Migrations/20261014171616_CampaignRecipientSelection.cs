using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevCoreBlog.Migrations
{
    /// <inheritdoc />
    public partial class CampaignRecipientSelection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NewsletterRecipientSelections",
                columns: table => new
                {
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    CutoffUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpperSubscriberId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastSubscriberId = table.Column<Guid>(type: "uuid", nullable: true),
                    ScannedCount = table.Column<long>(type: "bigint", nullable: false),
                    SelectedCount = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewsletterRecipientSelections", x => x.CampaignId);
                    table.CheckConstraint("CK_RecipientSelections_Progress", "\"CampaignId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"Version\">0 AND \"State\" IN (0,1)\nAND isfinite(\"CutoffUtc\") AND isfinite(\"UpdatedUtc\") AND \"UpdatedUtc\">=\"CutoffUtc\"\nAND \"ScannedCount\">=0 AND \"SelectedCount\" BETWEEN 0 AND \"ScannedCount\"\nAND (\"UpperSubscriberId\" IS NULL OR \"UpperSubscriberId\"<>'00000000-0000-0000-0000-000000000000'::uuid)\nAND ((\"LastSubscriberId\" IS NULL AND \"ScannedCount\"=0) OR\n    (\"LastSubscriberId\" IS NOT NULL AND \"ScannedCount\">0 AND \"UpperSubscriberId\" IS NOT NULL\n     AND \"LastSubscriberId\">'00000000-0000-0000-0000-000000000000'::uuid AND \"LastSubscriberId\"<=\"UpperSubscriberId\"))");
                    table.ForeignKey(
                        name: "FK_NewsletterRecipientSelections_NewsletterCampaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "NewsletterCampaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NewsletterRecipients",
                columns: table => new
                {
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberVersion = table.Column<long>(type: "bigint", nullable: false),
                    SelectionVersion = table.Column<long>(type: "bigint", nullable: false),
                    PeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmailAddress = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false, collation: "C"),
                    AddressKey = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false, collation: "C"),
                    SelectedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewsletterRecipients", x => new { x.CampaignId, x.SubscriberId });
                    table.CheckConstraint("CK_NewsletterRecipients_Binding", "\"SubscriberId\"<>'00000000-0000-0000-0000-000000000000'::uuid AND \"PeriodId\"<>'00000000-0000-0000-0000-000000000000'::uuid\nAND \"SubscriberVersion\">0 AND \"SelectionVersion\">=2 AND isfinite(\"SelectedUtc\")\nAND octet_length(\"EmailAddress\") BETWEEN 3 AND 254 AND \"AddressKey\"=lower(\"EmailAddress\" COLLATE \"C\")");
                    table.ForeignKey(
                        name: "FK_NewsletterRecipients_NewsletterRecipientSelections_Campaign~",
                        column: x => x.CampaignId,
                        principalTable: "NewsletterRecipientSelections",
                        principalColumn: "CampaignId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NewsletterRecipients_Subscribers_SubscriberId",
                        column: x => x.SubscriberId,
                        principalTable: "Subscribers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NewsletterRecipients_CampaignId_AddressKey",
                table: "NewsletterRecipients",
                columns: new[] { "CampaignId", "AddressKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NewsletterRecipients_CampaignId_SelectionVersion",
                table: "NewsletterRecipients",
                columns: new[] { "CampaignId", "SelectionVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_NewsletterRecipients_SubscriberId",
                table: "NewsletterRecipients",
                column: "SubscriberId");

            migrationBuilder.Sql("""
                CREATE FUNCTION "GuardRecipientSelection"() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE campaign record; added bigint;
                BEGIN
                    IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Recipient history must be retained.' USING ERRCODE='23514'; END IF;
                    SELECT "State","CreatedUtc" INTO campaign FROM "NewsletterCampaigns" WHERE "Id"=NEW."CampaignId" FOR UPDATE;
                    IF NOT FOUND OR campaign."State"<>0 OR NEW."CutoffUtc"<campaign."CreatedUtc" THEN
                        RAISE EXCEPTION 'Campaign cannot select recipients.' USING ERRCODE='23514';
                    END IF;
                    IF TG_OP='INSERT' THEN
                        IF NEW."Version"<>1 OR NEW."State"<>0 OR NEW."ScannedCount"<>0 OR NEW."SelectedCount"<>0
                            OR NEW."LastSubscriberId" IS NOT NULL OR NEW."UpdatedUtc"<>NEW."CutoffUtc" THEN
                            RAISE EXCEPTION 'Invalid initial recipient selection.' USING ERRCODE='23514';
                        END IF;
                        RETURN NEW;
                    END IF;
                    IF (to_jsonb(NEW)-ARRAY['LastSubscriberId','ScannedCount','SelectedCount','State','UpdatedUtc','Version']) IS DISTINCT FROM
                        (to_jsonb(OLD)-ARRAY['LastSubscriberId','ScannedCount','SelectedCount','State','UpdatedUtc','Version'])
                        OR OLD."State"<>0 OR NEW."Version"<>OLD."Version"+1 OR NEW."UpdatedUtc"<OLD."UpdatedUtc"
                        OR NEW."ScannedCount"-OLD."ScannedCount" NOT BETWEEN 0 AND 250
                        OR NEW."SelectedCount"-OLD."SelectedCount" NOT BETWEEN 0 AND NEW."ScannedCount"-OLD."ScannedCount"
                        OR (NEW."ScannedCount"=OLD."ScannedCount" AND (NEW."LastSubscriberId" IS DISTINCT FROM OLD."LastSubscriberId" OR NEW."State"<>1))
                        OR (NEW."ScannedCount">OLD."ScannedCount" AND (NEW."LastSubscriberId" IS NULL OR NEW."LastSubscriberId"<=coalesce(OLD."LastSubscriberId",'00000000-0000-0000-0000-000000000000'::uuid))) THEN
                        RAISE EXCEPTION 'Invalid recipient selection progress.' USING ERRCODE='23514';
                    END IF;
                    SELECT count(*) INTO added FROM "NewsletterRecipients" WHERE "CampaignId"=NEW."CampaignId" AND "SelectionVersion"=NEW."Version";
                    IF added<>NEW."SelectedCount"-OLD."SelectedCount" THEN
                        RAISE EXCEPTION 'Recipient count does not match committed batch.' USING ERRCODE='23514';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER "TR_RecipientSelection_Progress" BEFORE INSERT OR UPDATE OR DELETE ON "NewsletterRecipientSelections"
                    FOR EACH ROW EXECUTE FUNCTION "GuardRecipientSelection"();

                CREATE FUNCTION "GuardNewsletterRecipient"() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE selection "NewsletterRecipientSelections"%ROWTYPE; subscriber "Subscribers"%ROWTYPE;
                BEGIN
                    IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'Selected recipient is immutable.' USING ERRCODE='23514'; END IF;
                    PERFORM 1 FROM "NewsletterCampaigns" WHERE "Id"=NEW."CampaignId" AND "State"=0 FOR UPDATE;
                    IF NOT FOUND THEN RAISE EXCEPTION 'Campaign cannot select recipients.' USING ERRCODE='23514'; END IF;
                    SELECT * INTO selection FROM "NewsletterRecipientSelections" WHERE "CampaignId"=NEW."CampaignId" FOR UPDATE;
                    IF NOT FOUND OR selection."State"<>0 OR NEW."SelectionVersion"<>selection."Version"+1
                        OR NEW."SelectedUtc"<selection."UpdatedUtc" OR selection."UpperSubscriberId" IS NULL
                        OR NEW."SubscriberId">selection."UpperSubscriberId"
                        OR NEW."SubscriberId"<=coalesce(selection."LastSubscriberId",'00000000-0000-0000-0000-000000000000'::uuid) THEN
                        RAISE EXCEPTION 'Recipient is outside this batch.' USING ERRCODE='23514';
                    END IF;
                    SELECT * INTO subscriber FROM "Subscribers" WHERE "Id"=NEW."SubscriberId" FOR SHARE;
                    IF NOT FOUND OR subscriber."Status"<>1 OR subscriber."VerifiedUtc" IS NULL OR subscriber."Suppression"<>0
                        OR subscriber."CreatedUtc">selection."CutoffUtc" OR subscriber."UpdatedUtc">selection."CutoffUtc"
                        OR NEW."SubscriberVersion"<>subscriber."Version" OR NEW."PeriodId"<>subscriber."PeriodId"
                        OR NEW."EmailAddress"<>subscriber."EmailAddress" OR NEW."AddressKey"<>subscriber."AddressKey" THEN
                        RAISE EXCEPTION 'Recipient lifecycle binding is invalid.' USING ERRCODE='23514';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER "TR_NewsletterRecipients_Binding" BEFORE INSERT OR UPDATE OR DELETE ON "NewsletterRecipients"
                    FOR EACH ROW EXECUTE FUNCTION "GuardNewsletterRecipient"();

                CREATE FUNCTION "RequireRecipientBatchCommit"() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM "NewsletterRecipientSelections" WHERE "CampaignId"=NEW."CampaignId" AND "Version">=NEW."SelectionVersion") THEN
                        RAISE EXCEPTION 'Recipients and progress must commit together.' USING ERRCODE='23514';
                    END IF;
                    RETURN NULL;
                END $$;
                CREATE CONSTRAINT TRIGGER "TR_NewsletterRecipients_BatchCommit" AFTER INSERT ON "NewsletterRecipients"
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION "RequireRecipientBatchCommit"();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE "NewsletterRecipientSelections", "NewsletterRecipients" IN ACCESS EXCLUSIVE MODE;
                DO $$ BEGIN
                    IF EXISTS(SELECT 1 FROM "NewsletterRecipientSelections") OR EXISTS(SELECT 1 FROM "NewsletterRecipients") THEN
                        RAISE EXCEPTION 'Recipient selection history must be retained; use forward repair.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "NewsletterRecipients");

            migrationBuilder.DropTable(
                name: "NewsletterRecipientSelections");
            migrationBuilder.Sql("DROP FUNCTION \"RequireRecipientBatchCommit\"(); DROP FUNCTION \"GuardNewsletterRecipient\"(); DROP FUNCTION \"GuardRecipientSelection\"();");
        }
    }
}
