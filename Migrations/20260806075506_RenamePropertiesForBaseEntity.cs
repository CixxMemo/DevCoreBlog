using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevCoreBlog.Migrations
{
    /// <inheritdoc />
    public partial class RenamePropertiesForBaseEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "IsPublished",
                table: "Posts",
                newName: "IsActive");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "Posts",
                newName: "CreatedDate");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedDate",
                table: "Categories",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Categories",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedDate",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Categories");

            migrationBuilder.RenameColumn(
                name: "IsActive",
                table: "Posts",
                newName: "IsPublished");

            migrationBuilder.RenameColumn(
                name: "CreatedDate",
                table: "Posts",
                newName: "CreatedAt");
        }
    }
}
