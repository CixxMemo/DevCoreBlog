-- Synthetic records in a database migrated through F20.
INSERT INTO "Categories" ("Id", "Name", "Slug", "CreatedDate", "IsActive")
VALUES (1001, 'F22 Original Category', 'shared-category', '2026-01-01T00:00:00Z', true);

INSERT INTO "Posts" (
    "Id", "Title", "Slug", "Summary", "Content", "CreatedDate", "IsActive",
    "IsPublished", "CategoryId", "ViewCount", "PublishDate", "Excerpt", "ThumbnailUrl"
) VALUES (
    2001, 'F22 Original Post', 'shared-post', 'old owner', 'old owner body',
    '2026-01-01T00:00:00Z', true, true, 1001, 0,
    '2026-01-01T00:00:00Z', '', ''
);
