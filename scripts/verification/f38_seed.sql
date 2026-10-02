-- Disposable F17 fixture only: ties, an older global winner and invisible high counts.
INSERT INTO "Posts" ("Id","Title","Slug","Summary","ThumbnailUrl","Excerpt","Content","IsPublished","PublishDate","CategoryId","ViewCount","CreatedDate","IsActive","EditVersion")
SELECT 3000+n, 'F38 Article '||n, 'f38-article-'||n, 'F38 discovery fixture', '', '', 'F38 public article', true, '2000-01-01'::timestamptz, 1001, CASE WHEN n=12 THEN 9000 WHEN n IN (1,2) THEN 1000 ELSE 100 END, '2000-01-01'::timestamptz, true, 1 FROM generate_series(1,12) n;
UPDATE "Posts" SET "ViewCount"=999999 WHERE "Id" IN (2002,2003,2004,2007,2008);
