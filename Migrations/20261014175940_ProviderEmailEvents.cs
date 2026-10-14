using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevCoreBlog.Migrations
{
    /// <inheritdoc />
    public partial class ProviderEmailEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProviderAddressSuppressions",
                columns: table => new
                {
                    AddressKey = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false, collation: "C"),
                    Flags = table.Column<int>(type: "integer", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderAddressSuppressions", x => x.AddressKey);
                    table.CheckConstraint("CK_ProviderAddressSuppressions_Valid", "octet_length(\"AddressKey\") BETWEEN 3 AND 254 AND \"AddressKey\"=lower(\"AddressKey\" COLLATE \"C\")\nAND \"Flags\" IN (1,2,3) AND isfinite(\"UpdatedUtc\")");
                });

            migrationBuilder.CreateTable(
                name: "ProviderEmailEvents",
                columns: table => new
                {
                    EventId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddressKey = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false, collation: "C"),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    OccurredUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderEmailEvents", x => x.EventId);
                    table.CheckConstraint("CK_ProviderEmailEvents_Valid", "\"EventId\" ~ '^[A-Za-z0-9_-]{1,128}$' AND \"PayloadHash\" ~ '^[A-Fa-f0-9]{64}$'\nAND \"MessageId\"<>'00000000-0000-0000-0000-000000000000'::uuid AND \"Kind\" IN (0,1,2,3)\nAND octet_length(\"AddressKey\") BETWEEN 3 AND 254 AND \"AddressKey\"=lower(\"AddressKey\" COLLATE \"C\")\nAND isfinite(\"OccurredUtc\") AND isfinite(\"ReceivedUtc\") AND \"OccurredUtc\"<=\"ReceivedUtc\"+interval '5 minutes'");
                });

            migrationBuilder.CreateTable(
                name: "ProviderEmailObservations",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddressKey = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false, collation: "C"),
                    DeliveredUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BouncedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ComplainedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TemporaryBounceUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderEmailObservations", x => new { x.MessageId, x.AddressKey });
                    table.CheckConstraint("CK_ProviderEmailObservations_Valid", "\"MessageId\"<>'00000000-0000-0000-0000-000000000000'::uuid\nAND octet_length(\"AddressKey\") BETWEEN 3 AND 254 AND \"AddressKey\"=lower(\"AddressKey\" COLLATE \"C\")\nAND (\"DeliveredUtc\" IS NOT NULL OR \"BouncedUtc\" IS NOT NULL OR \"ComplainedUtc\" IS NOT NULL OR \"TemporaryBounceUtc\" IS NOT NULL)\nAND (\"DeliveredUtc\" IS NULL OR isfinite(\"DeliveredUtc\")) AND (\"BouncedUtc\" IS NULL OR isfinite(\"BouncedUtc\"))\nAND (\"ComplainedUtc\" IS NULL OR isfinite(\"ComplainedUtc\")) AND (\"TemporaryBounceUtc\" IS NULL OR isfinite(\"TemporaryBounceUtc\"))");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderEmailEvents_MessageId_AddressKey",
                table: "ProviderEmailEvents",
                columns: new[] { "MessageId", "AddressKey" });
            migrationBuilder.Sql("""
                INSERT INTO "ProviderAddressSuppressions" ("AddressKey","Flags","UpdatedUtc")
                    SELECT "AddressKey","Suppression"&3,"UpdatedUtc" FROM "Subscribers" WHERE ("Suppression"&3)<>0;

                CREATE FUNCTION "GuardProviderEventHistory"() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Provider receipt history is immutable.' USING ERRCODE='23514'; END $$;
                CREATE TRIGGER "TR_ProviderEmailEvents_Immutable" BEFORE UPDATE OR DELETE ON "ProviderEmailEvents"
                    FOR EACH ROW EXECUTE FUNCTION "GuardProviderEventHistory"();

                CREATE FUNCTION "GuardProviderObservation"() RETURNS trigger LANGUAGE plpgsql AS $$
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
                CREATE TRIGGER "TR_ProviderEmailObservations_Facts" BEFORE INSERT OR UPDATE OR DELETE ON "ProviderEmailObservations"
                    FOR EACH ROW EXECUTE FUNCTION "GuardProviderObservation"();

                CREATE FUNCTION "GuardProviderSuppression"() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Provider suppression must be retained.' USING ERRCODE='23514'; END IF;
                    IF TG_OP='UPDATE' AND (NEW."AddressKey"<>OLD."AddressKey" OR (NEW."Flags"&OLD."Flags")<>OLD."Flags" OR NEW."UpdatedUtc"<OLD."UpdatedUtc") THEN
                        RAISE EXCEPTION 'Provider suppression cannot be cleared.' USING ERRCODE='23514';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER "TR_ProviderAddressSuppressions_Monotonic" BEFORE UPDATE OR DELETE ON "ProviderAddressSuppressions"
                    FOR EACH ROW EXECUTE FUNCTION "GuardProviderSuppression"();

                CREATE FUNCTION "GuardSubscriberProviderSuppression"() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE flags integer;
                BEGIN
                    IF TG_OP='INSERT' THEN
                        PERFORM pg_advisory_xact_lock(hashtextextended(NEW."AddressKey",362026));
                        SELECT "Flags" INTO flags FROM "ProviderAddressSuppressions" WHERE "AddressKey"=NEW."AddressKey";
                        NEW."Suppression"=NEW."Suppression"|coalesce(flags,0);
                    ELSE
                        IF (NEW."Suppression"&OLD."Suppression")<>OLD."Suppression" THEN
                            RAISE EXCEPTION 'Subscriber suppression cannot be cleared.' USING ERRCODE='23514';
                        END IF;
                        SELECT "Flags" INTO flags FROM "ProviderAddressSuppressions" WHERE "AddressKey"=NEW."AddressKey";
                        IF (NEW."Suppression"&coalesce(flags,0))<>coalesce(flags,0) THEN
                            RAISE EXCEPTION 'Subscriber must preserve provider suppression.' USING ERRCODE='23514';
                        END IF;
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER "TR_Subscribers_ProviderSuppression" BEFORE INSERT OR UPDATE ON "Subscribers"
                    FOR EACH ROW EXECUTE FUNCTION "GuardSubscriberProviderSuppression"();

                CREATE FUNCTION "RequireProviderEventCommit"() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE flags integer;
                BEGIN
                    IF NOT EXISTS(SELECT 1 FROM "ProviderEmailObservations" WHERE "MessageId"=NEW."MessageId" AND "AddressKey"=NEW."AddressKey"
                        AND CASE NEW."Kind" WHEN 0 THEN "DeliveredUtc" WHEN 1 THEN "BouncedUtc" WHEN 2 THEN "ComplainedUtc" ELSE "TemporaryBounceUtc" END <= NEW."OccurredUtc") THEN
                        RAISE EXCEPTION 'Receipt and observed fact must commit together.' USING ERRCODE='23514';
                    END IF;
                    flags=CASE NEW."Kind" WHEN 1 THEN 1 WHEN 2 THEN 2 ELSE 0 END;
                    IF flags<>0 AND (NOT EXISTS(SELECT 1 FROM "ProviderAddressSuppressions" WHERE "AddressKey"=NEW."AddressKey" AND ("Flags"&flags)=flags)
                        OR EXISTS(SELECT 1 FROM "Subscribers" WHERE "AddressKey"=NEW."AddressKey" AND ("Suppression"&flags)<>flags)) THEN
                        RAISE EXCEPTION 'Receipt and suppression must commit together.' USING ERRCODE='23514';
                    END IF;
                    RETURN NULL;
                END $$;
                CREATE CONSTRAINT TRIGGER "TR_ProviderEmailEvents_Commit" AFTER INSERT ON "ProviderEmailEvents"
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION "RequireProviderEventCommit"();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE "ProviderEmailEvents","ProviderEmailObservations","ProviderAddressSuppressions" IN ACCESS EXCLUSIVE MODE;
                DO $$ BEGIN
                    IF EXISTS(SELECT 1 FROM "ProviderEmailEvents") OR EXISTS(SELECT 1 FROM "ProviderEmailObservations") OR EXISTS(SELECT 1 FROM "ProviderAddressSuppressions") THEN
                        RAISE EXCEPTION 'Provider facts and suppression must be retained; use forward repair.';
                    END IF;
                END $$;
                DROP TRIGGER "TR_Subscribers_ProviderSuppression" ON "Subscribers";
                DROP FUNCTION "GuardSubscriberProviderSuppression"();
                """);
            migrationBuilder.DropTable(
                name: "ProviderAddressSuppressions");

            migrationBuilder.DropTable(
                name: "ProviderEmailEvents");

            migrationBuilder.DropTable(
                name: "ProviderEmailObservations");
            migrationBuilder.Sql("""
                DROP FUNCTION "RequireProviderEventCommit"(); DROP FUNCTION "GuardProviderEventHistory"();
                DROP FUNCTION "GuardProviderObservation"(); DROP FUNCTION "GuardProviderSuppression"();
                """);
        }
    }
}
