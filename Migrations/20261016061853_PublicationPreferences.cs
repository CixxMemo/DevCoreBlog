using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevCoreBlog.Migrations
{
    /// <inheritdoc />
    public partial class PublicationPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PublicationSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Cadence = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SenderEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true, collation: "C"),
                    SenderName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicationSettings", x => x.Id);
                    table.CheckConstraint("CK_PublicationSettings_Valid", "\"Id\"=1 AND \"Version\">0 AND isfinite(\"UpdatedUtc\")\nAND length(btrim(\"Name\")) BETWEEN 1 AND 80 AND \"Name\"=btrim(\"Name\") AND octet_length(\"Name\")<=320 AND \"Name\" !~ '[[:cntrl:]<>]'\nAND length(btrim(\"Cadence\")) BETWEEN 1 AND 200 AND \"Cadence\"=btrim(\"Cadence\") AND octet_length(\"Cadence\")<=800 AND \"Cadence\" !~ '[[:cntrl:]<>]'\nAND ((\"SenderEmail\" IS NULL AND \"SenderName\" IS NULL) OR (\"SenderEmail\" IS NOT NULL AND \"SenderName\" IS NOT NULL\n    AND length(\"SenderEmail\") BETWEEN 3 AND 254 AND \"SenderEmail\" ~ '^[!-~]+@[A-Za-z0-9.-]+$'\n    AND length(btrim(\"SenderName\")) BETWEEN 1 AND 80 AND octet_length(\"SenderName\")<=320 AND \"SenderName\" !~ '[[:cntrl:]<>\"\\\\]'))");
                });
            migrationBuilder.Sql("""
                CREATE FUNCTION "GuardPublicationSettings"() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM pg_advisory_xact_lock(422026);
                    IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Publication preferences must be retained; replace through CAS.' USING ERRCODE='23514'; END IF;
                    IF (TG_OP='INSERT' AND NEW."Version"<>1) OR (TG_OP='UPDATE' AND (NEW."Id"<>OLD."Id" OR NEW."Version"<>OLD."Version"+1 OR NEW."UpdatedUtc"<OLD."UpdatedUtc")) THEN
                        RAISE EXCEPTION 'Publication changes require monotonic version and time.' USING ERRCODE='23514';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER "TR_PublicationSettings_Guard" BEFORE INSERT OR UPDATE OR DELETE ON "PublicationSettings"
                    FOR EACH ROW EXECUTE FUNCTION "GuardPublicationSettings"();
                CREATE FUNCTION "RequirePublicationSenderApproval"() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM pg_advisory_xact_lock_shared(422026);
                    -- A committed start key retains its original result after a later preference change.
                    IF EXISTS(SELECT 1 FROM "NewsletterCampaigns" WHERE "StartKey"=NEW."StartKey") THEN RETURN NEW; END IF;
                    IF EXISTS(SELECT 1 FROM "PublicationSettings") AND NOT EXISTS(SELECT 1 FROM "PublicationSettings"
                        WHERE "SenderEmail"=NEW."SenderEmail" AND "SenderName"=NEW."SenderName") THEN
                        RAISE EXCEPTION 'The current publication sender must be approved.' USING ERRCODE='23514',CONSTRAINT='CK_PublicationSenderApproval';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER "TR_NewsletterCampaigns_PublicationSender" BEFORE INSERT ON "NewsletterCampaigns"
                    FOR EACH ROW EXECUTE FUNCTION "RequirePublicationSenderApproval"();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                SELECT pg_advisory_xact_lock(422026);
                LOCK TABLE "PublicationSettings" IN ACCESS EXCLUSIVE MODE;
                DO $$ BEGIN IF EXISTS(SELECT 1 FROM "PublicationSettings") THEN
                    RAISE EXCEPTION 'Publication preferences must be retained; use forward repair.';
                END IF; END $$;
                DROP TRIGGER "TR_NewsletterCampaigns_PublicationSender" ON "NewsletterCampaigns";
                DROP FUNCTION "RequirePublicationSenderApproval"();
                """);
            migrationBuilder.DropTable(
                name: "PublicationSettings");
            migrationBuilder.Sql("DROP FUNCTION \"GuardPublicationSettings\"();");
        }
    }
}
