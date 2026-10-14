using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevCoreBlog.Migrations
{
    /// <inheritdoc />
    public partial class SubscriberVerificationTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubscriberVerificationTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberVersion = table.Column<long>(type: "bigint", nullable: false),
                    Purpose = table.Column<int>(type: "integer", nullable: false),
                    Digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    AddressBinding = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriberVerificationTokens", x => x.Id);
                    table.CheckConstraint("CK_SubscriberVerificationTokens_Valid", "\"Purpose\" = 1 AND \"SubscriberVersion\" > 0\nAND \"Digest\" ~ '^[A-F0-9]{64}$' AND \"AddressBinding\" ~ '^[A-F0-9]{64}$'\nAND isfinite(\"CreatedUtc\") AND isfinite(\"ExpiresUtc\") AND \"ExpiresUtc\" = \"CreatedUtc\" + interval '86400 seconds'\nAND (\"ConsumedUtc\" IS NULL OR (isfinite(\"ConsumedUtc\") AND \"ConsumedUtc\" >= \"CreatedUtc\" AND \"ConsumedUtc\" < \"ExpiresUtc\"))");
                    table.ForeignKey(
                        name: "FK_SubscriberVerificationTokens_Subscribers_SubscriberId",
                        column: x => x.SubscriberId,
                        principalTable: "Subscribers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubscriberVerificationTokens_TransactionalEmailJobs_Id",
                        column: x => x.Id,
                        principalTable: "TransactionalEmailJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriberVerificationTokens_Digest",
                table: "SubscriberVerificationTokens",
                column: "Digest",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriberVerificationTokens_SubscriberId_SubscriberVersion~",
                table: "SubscriberVerificationTokens",
                columns: new[] { "SubscriberId", "SubscriberVersion", "ExpiresUtc" },
                filter: "\"ConsumedUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE "SubscriberVerificationTokens" IN ACCESS EXCLUSIVE MODE;
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "SubscriberVerificationTokens") THEN
                        RAISE EXCEPTION 'Verification evidence must be retained; use a compatible forward repair.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "SubscriberVerificationTokens");
        }
    }
}
