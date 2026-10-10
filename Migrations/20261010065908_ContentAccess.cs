using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevCoreBlog.Migrations
{
    /// <inheritdoc />
    public partial class ContentAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AccessScope",
                table: "Posts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ContentKind",
                table: "Posts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Posts_AccessScope",
                table: "Posts",
                sql: "\"AccessScope\" IN (0, 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Posts_ContentKind",
                table: "Posts",
                sql: "\"ContentKind\" IN (0, 1, 2, 3, 4)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Posts_NewsletterAccess",
                table: "Posts",
                sql: "\"ContentKind\" <> 4 OR \"AccessScope\" = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Refuse a downgrade that would discard classification or expose private bodies.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "Posts" WHERE "ContentKind" <> 0 OR "AccessScope" <> 0) THEN
                        RAISE EXCEPTION 'Content access metadata must be retained; use a compatible forward repair.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "CK_Posts_AccessScope",
                table: "Posts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Posts_ContentKind",
                table: "Posts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Posts_NewsletterAccess",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "AccessScope",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "ContentKind",
                table: "Posts");
        }
    }
}
