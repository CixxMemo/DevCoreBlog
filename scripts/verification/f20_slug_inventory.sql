-- Read-only inventory. Review owner Ids before applying UniqueSlugs to real data.
SELECT 'Posts' AS table_name, "Slug" AS old_slug,
       min("Id") AS proposed_owner_id,
       array_agg("Id" ORDER BY "Id") AS row_ids
FROM "Posts"
WHERE btrim("Slug", E' \t\n\r') <> ''
GROUP BY "Slug"
HAVING count(*) > 1
ORDER BY "Slug";

SELECT 'Categories' AS table_name, "Slug" AS old_slug,
       min("Id") AS proposed_owner_id,
       array_agg("Id" ORDER BY "Id") AS row_ids
FROM "Categories"
WHERE btrim("Slug", E' \t\n\r') <> ''
GROUP BY "Slug"
HAVING count(*) > 1
ORDER BY "Slug";

SELECT 'Posts' AS table_name, array_agg("Id" ORDER BY "Id") AS blank_slug_ids
FROM "Posts" WHERE btrim("Slug", E' \t\n\r') = '';

SELECT 'Categories' AS table_name, array_agg("Id" ORDER BY "Id") AS blank_slug_ids
FROM "Categories" WHERE btrim("Slug", E' \t\n\r') = '';
