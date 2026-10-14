using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevCoreBlog.Migrations
{
    /// <inheritdoc />
    public partial class SubscriptionRegistrationAdmission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubscriptionRegistrationBudgets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    WindowStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionRegistrationBudgets", x => x.Id);
                    table.CheckConstraint("CK_SubscriptionRegistrationBudgets_Valid", "\"Id\"=1 AND \"Count\" BETWEEN 0 AND 20\nAND isfinite(\"WindowStartUtc\") AND isfinite(\"UpdatedUtc\")\nAND \"WindowStartUtc\" = date_trunc('day', \"WindowStartUtc\" AT TIME ZONE 'UTC') AT TIME ZONE 'UTC'\nAND \"UpdatedUtc\" >= \"WindowStartUtc\" AND \"UpdatedUtc\" < \"WindowStartUtc\" + interval '1 day'");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriberVerificationTokens_SubscriberId_CreatedUtc",
                table: "SubscriberVerificationTokens",
                columns: new[] { "SubscriberId", "CreatedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE "SubscriptionRegistrationBudgets" IN ACCESS EXCLUSIVE MODE;
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "SubscriptionRegistrationBudgets") THEN
                        RAISE EXCEPTION 'Registration admission evidence must be retained; use a compatible forward repair.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "SubscriptionRegistrationBudgets");

            migrationBuilder.DropIndex(
                name: "IX_SubscriberVerificationTokens_SubscriberId_CreatedUtc",
                table: "SubscriberVerificationTokens");
        }
    }
}
