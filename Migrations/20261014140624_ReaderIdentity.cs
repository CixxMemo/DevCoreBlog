using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevCoreBlog.Migrations
{
    /// <inheritdoc />
    public partial class ReaderIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Purpose",
                table: "TransactionalEmailJobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ReaderLoginBudgets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    WindowStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReaderLoginBudgets", x => x.Id);
                    table.CheckConstraint("CK_ReaderLoginBudgets_Valid", "\"Id\" = 1 AND \"Count\" BETWEEN 0 AND 20 AND isfinite(\"WindowStartUtc\") AND isfinite(\"UpdatedUtc\")\nAND \"WindowStartUtc\" = date_trunc('day', \"WindowStartUtc\" AT TIME ZONE 'UTC') AT TIME ZONE 'UTC'\nAND \"UpdatedUtc\" >= \"WindowStartUtc\" AND \"UpdatedUtc\" < \"WindowStartUtc\" + interval '1 day'");
                });

            migrationBuilder.CreateTable(
                name: "ReaderLoginTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberVersion = table.Column<long>(type: "bigint", nullable: false),
                    PeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    Digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AddressBinding = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReaderLoginTokens", x => x.Id);
                    table.CheckConstraint("CK_ReaderLoginTokens_Valid", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"SubscriberVersion\" > 0\nAND \"PeriodId\" <> '00000000-0000-0000-0000-000000000000'::uuid\nAND \"Digest\" ~ '^[A-F0-9]{64}$' AND \"AddressBinding\" ~ '^[A-F0-9]{64}$'\nAND isfinite(\"CreatedUtc\") AND isfinite(\"ExpiresUtc\") AND \"ExpiresUtc\" = \"CreatedUtc\" + interval '15 minutes'\nAND (\"ConsumedUtc\" IS NULL OR (isfinite(\"ConsumedUtc\") AND \"ConsumedUtc\" >= \"CreatedUtc\" AND \"ConsumedUtc\" < \"ExpiresUtc\"))");
                    table.ForeignKey(
                        name: "FK_ReaderLoginTokens_Subscribers_SubscriberId",
                        column: x => x.SubscriberId,
                        principalTable: "Subscribers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReaderLoginTokens_TransactionalEmailJobs_Id",
                        column: x => x.Id,
                        principalTable: "TransactionalEmailJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReaderSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberVersion = table.Column<long>(type: "bigint", nullable: false),
                    PeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReaderSessions", x => x.Id);
                    table.CheckConstraint("CK_ReaderSessions_Valid", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"SubscriberVersion\" > 0\nAND \"PeriodId\" <> '00000000-0000-0000-0000-000000000000'::uuid\nAND isfinite(\"CreatedUtc\") AND isfinite(\"ExpiresUtc\") AND \"ExpiresUtc\" = \"CreatedUtc\" + interval '7 days'\nAND (\"RevokedUtc\" IS NULL OR (isfinite(\"RevokedUtc\") AND \"RevokedUtc\" >= \"CreatedUtc\"))");
                    table.ForeignKey(
                        name: "FK_ReaderSessions_Subscribers_SubscriberId",
                        column: x => x.SubscriberId,
                        principalTable: "Subscribers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_TransactionalEmailJobs_Purpose",
                table: "TransactionalEmailJobs",
                sql: "\"Purpose\" IN (0,1)");

            migrationBuilder.CreateIndex(
                name: "IX_ReaderLoginTokens_Digest",
                table: "ReaderLoginTokens",
                column: "Digest",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReaderLoginTokens_SubscriberId_CreatedUtc",
                table: "ReaderLoginTokens",
                columns: new[] { "SubscriberId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReaderSessions_SubscriberId",
                table: "ReaderSessions",
                column: "SubscriberId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rollback cannot erase proofs, revocations, admission evidence or reinterpret login mail as verification.
            migrationBuilder.Sql("""
                LOCK TABLE "TransactionalEmailJobs", "ReaderLoginBudgets", "ReaderLoginTokens", "ReaderSessions" IN ACCESS EXCLUSIVE MODE;
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "ReaderLoginBudgets") OR EXISTS (SELECT 1 FROM "ReaderLoginTokens")
                        OR EXISTS (SELECT 1 FROM "ReaderSessions") OR EXISTS (SELECT 1 FROM "TransactionalEmailJobs" WHERE "Purpose" <> 0) THEN
                        RAISE EXCEPTION 'Reader identity history must be retained; use a forward repair.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "ReaderLoginBudgets");

            migrationBuilder.DropTable(
                name: "ReaderLoginTokens");

            migrationBuilder.DropTable(
                name: "ReaderSessions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TransactionalEmailJobs_Purpose",
                table: "TransactionalEmailJobs");

            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "TransactionalEmailJobs");
        }
    }
}
