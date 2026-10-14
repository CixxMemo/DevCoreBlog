using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevCoreBlog.Migrations
{
    /// <inheritdoc />
    public partial class NewsletterCampaignSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NewsletterCampaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StartKey = table.Column<Guid>(type: "uuid", nullable: false),
                    PostId = table.Column<int>(type: "integer", nullable: false),
                    SourceRevision = table.Column<long>(type: "bigint", nullable: false),
                    Subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SourceSlug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DocumentVersion = table.Column<int>(type: "integer", nullable: false),
                    DocumentJson = table.Column<string>(type: "text", nullable: false),
                    DocumentPlainText = table.Column<string>(type: "text", nullable: false),
                    DocumentWordCount = table.Column<int>(type: "integer", nullable: false),
                    DocumentReadingMinutes = table.Column<int>(type: "integer", nullable: false),
                    RenderVersion = table.Column<int>(type: "integer", nullable: false),
                    Html = table.Column<string>(type: "text", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    WebVersionUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    SenderEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false, collation: "C"),
                    SenderName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ReplyTo = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true, collation: "C"),
                    ProviderContract = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, collation: "C"),
                    SnapshotHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    State = table.Column<int>(type: "integer", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewsletterCampaigns", x => x.Id);
                    table.CheckConstraint("CK_NewsletterCampaigns_Content", "length(btrim(\"Subject\")) BETWEEN 1 AND 300 AND \"Subject\" !~ '[[:cntrl:]]'\nAND length(btrim(\"SourceSlug\")) BETWEEN 1 AND 200 AND \"SourceSlug\" !~ '[[:cntrl:]]'\nAND \"DocumentVersion\"=1 AND octet_length(\"DocumentJson\") BETWEEN 1 AND 1048576\nAND octet_length(\"DocumentPlainText\") <= 1048576 AND \"DocumentWordCount\" BETWEEN 0 AND 200000\nAND \"DocumentReadingMinutes\"=(\"DocumentWordCount\"+199)/200 AND \"RenderVersion\"=1\nAND octet_length(\"Html\") BETWEEN 1 AND 983040 AND octet_length(\"Text\") BETWEEN 1 AND 245760\nAND \"WebVersionUrl\" ~ '^https://' AND \"WebVersionUrl\" !~ '[[:space:][:cntrl:]]'");
                    table.CheckConstraint("CK_NewsletterCampaigns_Identity", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid\nAND \"StartKey\" <> '00000000-0000-0000-0000-000000000000'::uuid\nAND \"SourceRevision\" > 0 AND \"SourceRevision\" < 9223372036854775807\nAND \"SnapshotHash\" ~ '^[A-F0-9]{64}$'");
                    table.CheckConstraint("CK_NewsletterCampaigns_Sender", "length(btrim(\"SenderName\")) BETWEEN 1 AND 80 AND \"SenderName\" !~ '[[:cntrl:]<>\"\\\\]'\nAND length(\"SenderEmail\") BETWEEN 3 AND 254 AND \"SenderEmail\" ~ '^[!-~]+@[!-~]+\\.[!-~]+$'\nAND (\"ReplyTo\" IS NULL OR (length(\"ReplyTo\") BETWEEN 3 AND 254 AND \"ReplyTo\" ~ '^[!-~]+@[!-~]+\\.[!-~]+$'))\nAND \"ProviderContract\" ~ '^[A-Za-z0-9/.-]{1,32}$'");
                    table.CheckConstraint("CK_NewsletterCampaigns_State", "((\"State\"=0 AND \"Version\"=1 AND \"UpdatedUtc\"=\"CreatedUtc\") OR (\"State\"=1 AND \"Version\"=2))\nAND isfinite(\"CreatedUtc\") AND isfinite(\"UpdatedUtc\") AND \"UpdatedUtc\">=\"CreatedUtc\"");
                    table.ForeignKey(
                        name: "FK_NewsletterCampaigns_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NewsletterCampaigns_PostId",
                table: "NewsletterCampaigns",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_NewsletterCampaigns_StartKey",
                table: "NewsletterCampaigns",
                column: "StartKey",
                unique: true);

            migrationBuilder.Sql("""
                CREATE FUNCTION "GuardNewsletterCampaignSnapshot"() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP='DELETE' THEN
                        RAISE EXCEPTION 'Campaign history must be retained.' USING ERRCODE='23514';
                    END IF;
                    IF (to_jsonb(NEW)-ARRAY['State','UpdatedUtc','Version']) IS DISTINCT FROM
                        (to_jsonb(OLD)-ARRAY['State','UpdatedUtc','Version']) THEN
                        RAISE EXCEPTION 'Campaign snapshot is immutable.' USING ERRCODE='23514';
                    END IF;
                    IF NEW IS NOT DISTINCT FROM OLD THEN RETURN NEW; END IF;
                    IF OLD."State"<>0 OR NEW."State"<>1 OR NEW."Version"<>OLD."Version"+1
                        OR NEW."UpdatedUtc"<OLD."UpdatedUtc" THEN
                        RAISE EXCEPTION 'Invalid campaign transition.' USING ERRCODE='23514';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER "TR_NewsletterCampaigns_Immutable" BEFORE UPDATE OR DELETE ON "NewsletterCampaigns"
                    FOR EACH ROW EXECUTE FUNCTION "GuardNewsletterCampaignSnapshot"();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE "NewsletterCampaigns" IN ACCESS EXCLUSIVE MODE;
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "NewsletterCampaigns") THEN
                        RAISE EXCEPTION 'Campaign snapshots must be retained; use a compatible forward repair.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "NewsletterCampaigns");
            migrationBuilder.Sql("DROP FUNCTION \"GuardNewsletterCampaignSnapshot\"();");
        }
    }
}
