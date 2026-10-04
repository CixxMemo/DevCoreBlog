using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevCoreBlog.Migrations
{
    /// <inheritdoc />
    public partial class ReadOrderingIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Posts_CreatedDate_Id",
                table: "Posts",
                columns: new[] { "CreatedDate", "Id" },
                descending: new[] { true, false });

            migrationBuilder.CreateIndex(
                name: "IX_Posts_PublishDate_Id",
                table: "Posts",
                columns: new[] { "PublishDate", "Id" },
                descending: new[] { true, false },
                filter: "\"IsActive\" AND \"IsPublished\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Posts_CreatedDate_Id",
                table: "Posts");

            migrationBuilder.DropIndex(
                name: "IX_Posts_PublishDate_Id",
                table: "Posts");
        }
    }
}
