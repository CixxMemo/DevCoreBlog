using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevCoreBlog.Migrations
{
    /// <inheritdoc />
    public partial class ScopedWebhookReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_WebhookReceipts",
                table: "WebhookReceipts");

            migrationBuilder.AddColumn<int>(
                name: "ContractVersion",
                table: "WebhookReceipts",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddPrimaryKey(
                name: "PK_WebhookReceipts",
                table: "WebhookReceipts",
                columns: new[] { "ContractVersion", "Key" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_WebhookReceipts_ContractVersion",
                table: "WebhookReceipts",
                sql: "\"ContractVersion\" IN (1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Removing the namespace can merge durable identities or discard v2 history.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "WebhookReceipts" WHERE "ContractVersion" = 2) THEN
                        RAISE EXCEPTION 'Versioned webhook receipts must be retained; use a compatible forward repair.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropPrimaryKey(
                name: "PK_WebhookReceipts",
                table: "WebhookReceipts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WebhookReceipts_ContractVersion",
                table: "WebhookReceipts");

            migrationBuilder.DropColumn(
                name: "ContractVersion",
                table: "WebhookReceipts");

            migrationBuilder.AddPrimaryKey(
                name: "PK_WebhookReceipts",
                table: "WebhookReceipts",
                column: "Key");
        }
    }
}
