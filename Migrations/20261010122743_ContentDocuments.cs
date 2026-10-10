using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevCoreBlog.Migrations
{
    /// <inheritdoc />
    public partial class ContentDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DocumentJson",
                table: "Posts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentPlainText",
                table: "Posts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DocumentReadingMinutes",
                table: "Posts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DocumentVersion",
                table: "Posts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DocumentWordCount",
                table: "Posts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Posts_DocumentFacts",
                table: "Posts",
                sql: "(\"DocumentVersion\" IS NULL AND \"DocumentJson\" IS NULL AND \"DocumentPlainText\" IS NULL\n    AND \"DocumentWordCount\" IS NULL AND \"DocumentReadingMinutes\" IS NULL)\nOR\n(\"DocumentVersion\" IS NOT NULL AND \"DocumentJson\" IS NOT NULL AND \"DocumentPlainText\" IS NOT NULL\n    AND \"DocumentWordCount\" IS NOT NULL AND \"DocumentReadingMinutes\" IS NOT NULL\n    AND \"DocumentVersion\" = 1 AND octet_length(\"DocumentJson\") BETWEEN 1 AND 1048576\n    AND octet_length(\"DocumentPlainText\") <= 1048576\n    AND \"DocumentWordCount\" BETWEEN 0 AND 200000\n    AND \"DocumentReadingMinutes\" = (\"DocumentWordCount\" + 199) / 200)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Serialize the check with writes; never discard a document or any of its facts.
            migrationBuilder.Sql("""
                LOCK TABLE "Posts" IN ACCESS EXCLUSIVE MODE;
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "Posts" WHERE "DocumentVersion" IS NOT NULL
                        OR "DocumentJson" IS NOT NULL OR "DocumentPlainText" IS NOT NULL
                        OR "DocumentWordCount" IS NOT NULL OR "DocumentReadingMinutes" IS NOT NULL) THEN
                        RAISE EXCEPTION 'Document data must be retained; use a compatible forward repair.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "CK_Posts_DocumentFacts",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "DocumentJson",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "DocumentPlainText",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "DocumentReadingMinutes",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "DocumentVersion",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "DocumentWordCount",
                table: "Posts");
        }
    }
}
