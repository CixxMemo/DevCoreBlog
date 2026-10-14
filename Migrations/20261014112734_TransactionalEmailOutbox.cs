using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevCoreBlog.Migrations
{
    /// <inheritdoc />
    public partial class TransactionalEmailOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TransactionalEmailJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriberVersion = table.Column<long>(type: "bigint", nullable: false),
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
                    ProviderMessageId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionalEmailJobs", x => x.Id);
                    table.CheckConstraint("CK_TransactionalEmailJobs_Payload", "octet_length(\"ProtectedPayload\") BETWEEN 1 AND 4194304\nAND \"ProtectedPayload\" ~ '^[A-Za-z0-9+/=]+$' AND \"PayloadHash\" ~ '^[A-F0-9]{64}$'");
                    table.CheckConstraint("CK_TransactionalEmailJobs_State", "\"State\" BETWEEN 0 AND 7 AND \"Problem\" BETWEEN 0 AND 9 AND \"SubscriberVersion\" > 0\nAND \"AttemptCount\" BETWEEN 0 AND 5\nAND ((\"AttemptCount\" = 0) = (\"FirstAttemptUtc\" IS NULL))\nAND ((\"State\" IN (1,2)) = (\"LeaseId\" IS NOT NULL AND \"LeaseUntilUtc\" IS NOT NULL))\nAND ((\"LeaseId\" IS NULL) = (\"LeaseUntilUtc\" IS NULL))\nAND (\"State\" <> 2 OR \"AttemptCount\" > 0)\nAND ((\"State\" = 3) = (\"ProviderMessageId\" IS NOT NULL))\nAND (\"ProviderMessageId\" IS NULL OR \"ProviderMessageId\" <> '00000000-0000-0000-0000-000000000000'::uuid)");
                    table.CheckConstraint("CK_TransactionalEmailJobs_Time", "isfinite(\"CreatedUtc\") AND isfinite(\"UpdatedUtc\") AND isfinite(\"ExpiresUtc\") AND isfinite(\"DueUtc\")\nAND \"UpdatedUtc\" >= \"CreatedUtc\" AND \"ExpiresUtc\" >= \"CreatedUtc\" + interval '30 seconds'\nAND \"ExpiresUtc\" <= \"CreatedUtc\" + interval '1 day' AND \"DueUtc\" >= \"CreatedUtc\"\nAND (\"FirstAttemptUtc\" IS NULL OR (isfinite(\"FirstAttemptUtc\") AND \"FirstAttemptUtc\" BETWEEN \"CreatedUtc\" AND \"UpdatedUtc\"))\nAND (\"LeaseUntilUtc\" IS NULL OR isfinite(\"LeaseUntilUtc\"))");
                    table.ForeignKey(
                        name: "FK_TransactionalEmailJobs_Subscribers_SubscriberId",
                        column: x => x.SubscriberId,
                        principalTable: "Subscribers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TransactionalEmailAttempts",
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
                    table.PrimaryKey("PK_TransactionalEmailAttempts", x => new { x.JobId, x.Number });
                    table.CheckConstraint("CK_TransactionalEmailAttempts_Valid", "\"Number\" BETWEEN 1 AND 5 AND isfinite(\"StartedUtc\")\nAND ((\"CompletedUtc\" IS NULL) = (\"Outcome\" IS NULL))\nAND ((\"Outcome\" IS NULL) = (\"Reason\" IS NULL))\nAND (\"Outcome\" IS NULL OR \"Outcome\" BETWEEN 0 AND 4)\nAND (\"Reason\" IS NULL OR \"Reason\" BETWEEN 0 AND 15)\nAND (\"CompletedUtc\" IS NULL OR (isfinite(\"CompletedUtc\") AND \"CompletedUtc\" >= \"StartedUtc\"))\nAND (\"HttpStatus\" IS NULL OR \"HttpStatus\" BETWEEN 100 AND 599)\nAND (\"ProviderMessageId\" IS NULL OR (\"Outcome\" = 0 AND \"ProviderMessageId\" <> '00000000-0000-0000-0000-000000000000'::uuid))");
                    table.ForeignKey(
                        name: "FK_TransactionalEmailAttempts_TransactionalEmailJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "TransactionalEmailJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TransactionalEmailJobs_DueUtc_Id",
                table: "TransactionalEmailJobs",
                columns: new[] { "DueUtc", "Id" },
                filter: "\"State\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionalEmailJobs_LeaseUntilUtc",
                table: "TransactionalEmailJobs",
                column: "LeaseUntilUtc",
                filter: "\"State\" IN (1,2)");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionalEmailJobs_SubscriberId",
                table: "TransactionalEmailJobs",
                column: "SubscriberId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE "TransactionalEmailJobs", "TransactionalEmailAttempts" IN ACCESS EXCLUSIVE MODE;
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "TransactionalEmailJobs") OR EXISTS (SELECT 1 FROM "TransactionalEmailAttempts") THEN
                        RAISE EXCEPTION 'Durable email evidence must be retained; use a compatible forward repair.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "TransactionalEmailAttempts");

            migrationBuilder.DropTable(
                name: "TransactionalEmailJobs");
        }
    }
}
