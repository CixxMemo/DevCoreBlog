\set ON_ERROR_STOP on
-- Only the disposable F48 cluster may consume this representative synthetic data.
DO $$ BEGIN
    IF current_database() <> 'devcoreblog_f01_test' OR current_setting('port') <> '55449' THEN
        RAISE EXCEPTION 'Not the F48 fixture';
    END IF;
END $$;
INSERT INTO "Posts" ("Id","Title","Slug","Summary","ThumbnailUrl","Excerpt","IsPublished","Content",
    "ViewCount","PublishDate","CategoryId","CreatedDate","IsActive","EditVersion")
SELECT 10000+n, 'F48 needle article '||n, 'f48-post-'||n, 'Synthetic F48 summary', '', '', n%5<>0,
    repeat('Synthetic paragraph for repository measurement. ', 150), n%100,
    '2026-10-04Z'::timestamptz - n*interval '1 minute', CASE WHEN n%10=0 THEN 1002 ELSE 1001 END,
    '2026-09-01Z'::timestamptz - n*interval '1 minute', n%7<>0, 1
FROM generate_series(1,20000) n;
ANALYZE "Posts";
ANALYZE "Categories";
