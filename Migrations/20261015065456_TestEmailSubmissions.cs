using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevCoreBlog.Migrations
{
    /// <inheritdoc />
    public partial class TestEmailSubmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TestEmailSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InputHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    AddressKey = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false, collation: "C"),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Outcome = table.Column<int>(type: "integer", nullable: true),
                    Reason = table.Column<int>(type: "integer", nullable: true),
                    HttpStatus = table.Column<int>(type: "integer", nullable: true),
                    ProviderMessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestEmailSubmissions", x => x.Id);
                    table.CheckConstraint("CK_TestEmailSubmissions_Valid", "\"Id\"<>'00000000-0000-0000-0000-000000000000'::uuid AND \"InputHash\" ~ '^[A-F0-9]{64}$'\nAND length(\"AddressKey\") BETWEEN 3 AND 254 AND \"AddressKey\"=lower(\"AddressKey\") AND \"AddressKey\" ~ '^[!-~]+@[A-Za-z0-9.-]+$'\nAND isfinite(\"CreatedUtc\") AND ((\"CompletedUtc\" IS NULL)=(\"Outcome\" IS NULL)) AND ((\"Outcome\" IS NULL)=(\"Reason\" IS NULL))\nAND ((\"Version\"=1 AND \"CompletedUtc\" IS NULL) OR (\"Version\"=2 AND \"CompletedUtc\" IS NOT NULL))\nAND (\"CompletedUtc\" IS NULL OR (isfinite(\"CompletedUtc\") AND \"CompletedUtc\">=\"CreatedUtc\" AND \"CompletedUtc\"<\"CreatedUtc\"+interval '1 minute'))\nAND (\"Outcome\" IS NULL OR \"Outcome\" BETWEEN 0 AND 4) AND (\"Reason\" IS NULL OR \"Reason\" BETWEEN 0 AND 15)\nAND (\"HttpStatus\" IS NULL OR \"HttpStatus\" BETWEEN 100 AND 599)\nAND (\"CompletedUtc\" IS NOT NULL OR \"HttpStatus\" IS NULL)\nAND ((\"Outcome\" IS NOT NULL AND \"Outcome\"=0 AND \"ProviderMessageId\" IS NOT NULL AND \"ProviderMessageId\"<>'00000000-0000-0000-0000-000000000000'::uuid)\n    OR ((\"Outcome\" IS NULL OR \"Outcome\"<>0) AND \"ProviderMessageId\" IS NULL))");
                });

            migrationBuilder.CreateIndex(
                name: "IX_TestEmailSubmissions_CreatedUtc",
                table: "TestEmailSubmissions",
                column: "CreatedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TestEmailSubmissions_ProviderMessageId",
                table: "TestEmailSubmissions",
                column: "ProviderMessageId",
                unique: true,
                filter: "\"ProviderMessageId\" IS NOT NULL");
            migrationBuilder.Sql("""
                CREATE FUNCTION "GuardTestEmailSubmission"() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Test submission history must be retained.' USING ERRCODE='23514'; END IF;
                    IF TG_OP='INSERT' THEN
                        PERFORM pg_advisory_xact_lock(383826);
                        IF NEW."Version"<>1 OR NEW."CompletedUtc" IS NOT NULL OR NEW."Outcome" IS NOT NULL OR NEW."Reason" IS NOT NULL
                            OR NEW."HttpStatus" IS NOT NULL OR NEW."ProviderMessageId" IS NOT NULL THEN
                            RAISE EXCEPTION 'A test must begin with an unfinished durable intent.' USING ERRCODE='23514';
                        END IF;
                        IF EXISTS(SELECT 1 FROM "TestEmailSubmissions" WHERE "CreatedUtc">NEW."CreatedUtc"-interval '1 minute')
                            OR (SELECT count(*) FROM "TestEmailSubmissions" WHERE "CreatedUtc">NEW."CreatedUtc"-interval '1 day')>=3 THEN
                            RAISE EXCEPTION 'Test start exceeds the durable rolling budget or moves time backwards.' USING ERRCODE='23514';
                        END IF;
                        PERFORM pg_advisory_xact_lock(hashtextextended(NEW."AddressKey",362026));
                        PERFORM 1 FROM "Subscribers" WHERE "AddressKey"=NEW."AddressKey" FOR UPDATE;
                        IF EXISTS(SELECT 1 FROM "Subscribers" WHERE "AddressKey"=NEW."AddressKey" AND ("Status"=2 OR "Suppression"<>0 OR "UpdatedUtc">NEW."CreatedUtc"))
                            OR EXISTS(SELECT 1 FROM "ProviderAddressSuppressions" WHERE "AddressKey"=NEW."AddressKey") THEN
                            RAISE EXCEPTION 'Current source or provider suppression prevents the test.' USING ERRCODE='23514';
                        END IF;
                    ELSE
                        IF OLD."CompletedUtc" IS NOT NULL OR NEW."CompletedUtc" IS NULL OR NEW."Version"<>OLD."Version"+1
                            OR ROW(NEW."Id",NEW."InputHash",NEW."AddressKey",NEW."CreatedUtc") IS DISTINCT FROM ROW(OLD."Id",OLD."InputHash",OLD."AddressKey",OLD."CreatedUtc") THEN
                            RAISE EXCEPTION 'Only the unfinished test result may be completed once; intent and history are immutable.' USING ERRCODE='23514';
                        END IF;
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER "TR_TestEmailSubmissions_Guard" BEFORE INSERT OR UPDATE OR DELETE ON "TestEmailSubmissions"
                    FOR EACH ROW EXECUTE FUNCTION "GuardTestEmailSubmission"();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE "TestEmailSubmissions" IN ACCESS EXCLUSIVE MODE;
                DO $$ BEGIN IF EXISTS(SELECT 1 FROM "TestEmailSubmissions") THEN
                    RAISE EXCEPTION 'Test submission history must be retained; use forward repair.';
                END IF; END $$;
                """);
            migrationBuilder.DropTable(
                name: "TestEmailSubmissions");
            migrationBuilder.Sql("DROP FUNCTION \"GuardTestEmailSubmission\"();");
        }
    }
}
