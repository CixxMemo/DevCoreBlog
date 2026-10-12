using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevCoreBlog.Migrations
{
    /// <inheritdoc />
    public partial class SubscriberLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Subscribers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmailAddress = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false, collation: "C"),
                    AddressKey = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false, collation: "C"),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Suppression = table.Column<int>(type: "integer", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VerifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UnsubscribedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscribers", x => x.Id);
                    table.CheckConstraint("CK_Subscribers_Address", "octet_length(\"EmailAddress\") BETWEEN 3 AND 254\nAND \"EmailAddress\" ~ '^[A-Za-z0-9!#$%&''*+/=?^_`{|}~.-]+@[A-Za-z0-9-]+(\\.[A-Za-z0-9-]+)+$'\nAND length(split_part(\"EmailAddress\", '@', 1)) BETWEEN 1 AND 64\nAND split_part(\"EmailAddress\", '@', 1) NOT LIKE '.%'\nAND split_part(\"EmailAddress\", '@', 1) NOT LIKE '%.'\nAND position('..' in split_part(\"EmailAddress\", '@', 1)) = 0\nAND split_part(\"EmailAddress\", '@', 2) !~ '(^|\\.)-|-(\\.|$)|[A-Za-z0-9-]{64}'\nAND \"AddressKey\" = lower(\"EmailAddress\" COLLATE \"C\")");
                    table.CheckConstraint("CK_Subscribers_State", "\"Status\" IN (0,1,2) AND \"Suppression\" BETWEEN 0 AND 7 AND \"Version\" > 0\nAND (\"Status\" <> 0 OR \"VerifiedUtc\" IS NULL)\nAND (\"Status\" <> 1 OR \"VerifiedUtc\" IS NOT NULL)\nAND (\"Status\" <> 2 OR \"UnsubscribedUtc\" IS NOT NULL)\nAND (\"Status\" <> 1 OR \"UnsubscribedUtc\" IS NULL OR \"VerifiedUtc\" >= \"UnsubscribedUtc\")");
                    table.CheckConstraint("CK_Subscribers_Time", "isfinite(\"CreatedUtc\") AND isfinite(\"UpdatedUtc\") AND \"UpdatedUtc\" >= \"CreatedUtc\"\nAND (\"VerifiedUtc\" IS NULL OR (isfinite(\"VerifiedUtc\") AND \"VerifiedUtc\" BETWEEN \"CreatedUtc\" AND \"UpdatedUtc\"))\nAND (\"UnsubscribedUtc\" IS NULL OR (isfinite(\"UnsubscribedUtc\") AND \"UnsubscribedUtc\" BETWEEN \"CreatedUtc\" AND \"UpdatedUtc\"))");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Subscribers_AddressKey",
                table: "Subscribers",
                column: "AddressKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Empty-table rollback only; keep all subscription identities and suppression history.
            migrationBuilder.Sql("""
                LOCK TABLE "Subscribers" IN ACCESS EXCLUSIVE MODE;
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "Subscribers") THEN
                        RAISE EXCEPTION 'Subscriber data must be retained; use a compatible forward repair.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "Subscribers");
        }
    }
}
