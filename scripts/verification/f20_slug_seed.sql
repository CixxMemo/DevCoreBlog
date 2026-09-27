-- Synthetic prior-schema rows with duplicate, reserved suffix and blank slugs.
INSERT INTO "Categories" ("Id", "Name", "Slug", "CreatedDate", "IsActive") VALUES
    (1001, 'F20 Old Owner', 'shared-category', '2026-01-01T00:00:00Z', true),
    (1002, 'F20 Duplicate', 'shared-category', '2026-01-02T00:00:00Z', true),
    (1003, 'F20 Reserved', 'shared-category-2', '2026-01-03T00:00:00Z', true),
    (1004, 'F20 Blank', E'\t', '2026-01-04T00:00:00Z', true),
    (1005, 'F20 Fallback Reserved', 'category-1004', '2026-01-05T00:00:00Z', true);

INSERT INTO "Posts" (
    "Id", "Title", "Slug", "Summary", "Content", "CreatedDate", "IsActive",
    "IsPublished", "CategoryId", "ViewCount", "PublishDate", "Excerpt", "ThumbnailUrl"
) VALUES
    (2001, 'F20 Old Owner', 'shared-post', 'old owner', 'old owner body',
     '2026-01-01T00:00:00Z', true, true, 1001, 0, '2026-01-01T00:00:00Z', '', ''),
    (2002, 'F20 Duplicate', 'shared-post', 'duplicate', 'duplicate body',
     '2026-01-02T00:00:00Z', true, true, 1001, 0, '2026-01-02T00:00:00Z', '', ''),
    (2003, 'F20 Reserved', 'shared-post-2', 'reserved', 'reserved body',
     '2026-01-03T00:00:00Z', true, true, 1001, 0, '2026-01-03T00:00:00Z', '', ''),
    (2004, 'F20 Blank', E'\t', 'blank', 'blank body',
     '2026-01-04T00:00:00Z', true, true, 1001, 0, '2026-01-04T00:00:00Z', '', ''),
    (2005, 'F20 Fallback Reserved', 'post-2004', 'reserved fallback', 'reserved body',
     '2026-01-05T00:00:00Z', true, true, 1001, 0, '2026-01-05T00:00:00Z', '', '');
