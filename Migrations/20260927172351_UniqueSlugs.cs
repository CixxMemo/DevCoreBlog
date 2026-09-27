using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevCoreBlog.Migrations
{
    /// <inheritdoc />
    public partial class UniqueSlugs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Keep the lowest Id for every nonblank old URL. Resolve only the
            // remaining rows, while reserving all original addresses first.
            migrationBuilder.Sql("""
                LOCK TABLE "Posts", "Categories" IN ACCESS EXCLUSIVE MODE;

                DO $post_slugs$
                DECLARE
                    conflict record;
                    stem text;
                    candidate text;
                    suffix integer;
                BEGIN
                    FOR conflict IN
                        SELECT "Id", "Slug" FROM (
                            SELECT "Id", "Slug",
                                   row_number() OVER (PARTITION BY "Slug" ORDER BY "Id") AS owner_rank
                            FROM "Posts"
                        ) AS existing
                        WHERE btrim("Slug", E' \t\n\r') = '' OR owner_rank > 1
                        ORDER BY "Id"
                    LOOP
                        stem := CASE WHEN btrim(conflict."Slug", E' \t\n\r') = ''
                                     THEN 'post' ELSE conflict."Slug" END;
                        suffix := CASE WHEN btrim(conflict."Slug", E' \t\n\r') = ''
                                       THEN conflict."Id" ELSE 2 END;
                        candidate := stem || '-' || suffix;
                        WHILE EXISTS (SELECT 1 FROM "Posts" WHERE "Slug" = candidate) LOOP
                            suffix := suffix + 1;
                            candidate := stem || '-' || suffix;
                        END LOOP;
                        UPDATE "Posts" SET "Slug" = candidate WHERE "Id" = conflict."Id";
                    END LOOP;
                END
                $post_slugs$;

                DO $category_slugs$
                DECLARE
                    conflict record;
                    stem text;
                    candidate text;
                    suffix integer;
                BEGIN
                    FOR conflict IN
                        SELECT "Id", "Slug" FROM (
                            SELECT "Id", "Slug",
                                   row_number() OVER (PARTITION BY "Slug" ORDER BY "Id") AS owner_rank
                            FROM "Categories"
                        ) AS existing
                        WHERE btrim("Slug", E' \t\n\r') = '' OR owner_rank > 1
                        ORDER BY "Id"
                    LOOP
                        stem := CASE WHEN btrim(conflict."Slug", E' \t\n\r') = ''
                                     THEN 'category' ELSE conflict."Slug" END;
                        suffix := CASE WHEN btrim(conflict."Slug", E' \t\n\r') = ''
                                       THEN conflict."Id" ELSE 2 END;
                        candidate := stem || '-' || suffix;
                        WHILE EXISTS (SELECT 1 FROM "Categories" WHERE "Slug" = candidate) LOOP
                            suffix := suffix + 1;
                            candidate := stem || '-' || suffix;
                        END LOOP;
                        UPDATE "Categories" SET "Slug" = candidate WHERE "Id" = conflict."Id";
                    END LOOP;
                END
                $category_slugs$;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Posts_Slug",
                table: "Posts",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_Slug",
                table: "Categories",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "Slug ownership cannot be reversed automatically; restore a verified backup or apply a forward fix.");
        }
    }
}
