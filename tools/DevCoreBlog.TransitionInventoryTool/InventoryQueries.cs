/// <summary>Fixed aggregate queries; no raw title, slug, body, URL, key or credential leaves PostgreSQL.</summary>
internal static class InventoryQueries
{
    public const string Environment = """
        SELECT jsonb_build_object(
          'serverVersion', current_setting('server_version'),
          'serverVersionNumber', current_setting('server_version_num')::int,
          'databaseCollation', d.datcollate, 'databaseCtype', d.datctype,
          'collationProvider', to_jsonb(d)->>'datlocprovider',
          'locale', coalesce(to_jsonb(d)->>'datlocale', to_jsonb(d)->>'daticulocale'),
          'encoding', pg_encoding_to_char(d.encoding),
          'transactionReadOnly', current_setting('transaction_read_only'),
          'defaultTransactionReadOnly', current_setting('default_transaction_read_only'),
          'isolation', current_setting('transaction_isolation'),
          'statementTimeout', current_setting('statement_timeout'),
          'lockTimeout', current_setting('lock_timeout'),
          'roleIsSuperuser', r.rolsuper, 'roleCanCreateDatabase', r.rolcreatedb,
          'serverIsInRecovery', pg_is_in_recovery(),
          'snapshotUtc', transaction_timestamp(),
          'turkishILikeObservations', jsonb_build_object(
            'i_matches_I', 'i' ILIKE 'I', 'i_matches_dotted_I', 'i' ILIKE 'İ',
            'dotless_i_matches_I', 'ı' ILIKE 'I', 'dotless_i_matches_dotted_I', 'ı' ILIKE 'İ'))::text
        FROM pg_database d JOIN pg_roles r ON r.rolname = current_user
        WHERE d.datname = current_database()
        """;

    public const string Schema = """
        SELECT jsonb_build_object(
          'postsPresent', to_regclass('public."Posts"') IS NOT NULL,
          'categoriesPresent', to_regclass('public."Categories"') IS NOT NULL,
          'receiptsPresent', to_regclass('public."WebhookReceipts"') IS NOT NULL,
          'migrationsPresent', to_regclass('public."__EFMigrationsHistory"') IS NOT NULL,
          'postColumns', coalesce((SELECT jsonb_agg(column_name ORDER BY ordinal_position)
            FROM information_schema.columns WHERE table_schema='public' AND table_name='Posts'
              AND column_name IN ('Id','Title','Content','Summary','Excerpt','Slug','CategoryId',
                'IsActive','IsPublished','PublishDate','ThumbnailUrl','ThumbnailPublicId',
                'ThumbnailWidth','ThumbnailHeight','ThumbnailAlt','EditVersion','UpdatedDate')), '[]'),
          'postSlugUniqueValidUnfiltered', EXISTS(SELECT 1 FROM pg_index i
            JOIN pg_attribute a ON a.attrelid=i.indrelid AND a.attnum=ANY(i.indkey)
            WHERE i.indrelid=to_regclass('public."Posts"') AND i.indisunique AND i.indisvalid
              AND i.indisready AND i.indnkeyatts=1 AND i.indpred IS NULL AND i.indexprs IS NULL AND a.attname='Slug'),
          'categorySlugUniqueValidUnfiltered', EXISTS(SELECT 1 FROM pg_index i
            JOIN pg_attribute a ON a.attrelid=i.indrelid AND a.attnum=ANY(i.indkey)
            WHERE i.indrelid=to_regclass('public."Categories"') AND i.indisunique AND i.indisvalid
              AND i.indisready AND i.indnkeyatts=1 AND i.indpred IS NULL AND i.indexprs IS NULL AND a.attname='Slug'))::text
        """;

    public const string Totals = """
        SELECT jsonb_build_object('posts',count(*),
          'contentBytes',coalesce(sum(octet_length("Content")),0),
          'categories',(SELECT count(*) FROM public."Categories"))::text FROM public."Posts"
        """;

    public static string Posts(IReadOnlySet<string> columns)
    {
        // Optional expressions are selected solely from fixed identifiers, never external SQL input.
        var identity = columns.Contains("ThumbnailPublicId")
            ? "count(*) FILTER (WHERE nullif(btrim(p.\"ThumbnailPublicId\"),'') IS NOT NULL)" : "NULL::bigint";
        var dimensions = columns.Contains("ThumbnailWidth") && columns.Contains("ThumbnailHeight")
            ? "count(*) FILTER (WHERE p.\"ThumbnailWidth\">0 AND p.\"ThumbnailHeight\">0)" : "NULL::bigint";
        return $$"""
            SELECT jsonb_build_object(
              'storageContract','legacy_markdown_text_not_inferred_json',
              'total',count(*), 'active',count(*) FILTER (WHERE p."IsActive"),
              'inactiveByPostOrCategory',count(*) FILTER (WHERE NOT p."IsActive" OR NOT coalesce(c."IsActive",false)),
              'draft',count(*) FILTER (WHERE p."IsActive" AND c."IsActive" AND NOT p."IsPublished"),
              'scheduled',count(*) FILTER (WHERE p."IsActive" AND c."IsActive" AND p."IsPublished" AND p."PublishDate">transaction_timestamp()),
              'publiclyVisible',count(*) FILTER (WHERE p."IsActive" AND c."IsActive" AND p."IsPublished" AND p."PublishDate"<=transaction_timestamp()),
              'missingCategory',count(*) FILTER (WHERE c."Id" IS NULL),
              'blankTitle',count(*) FILTER (WHERE nullif(btrim(p."Title"),'') IS NULL),
              'blankContent',count(*) FILTER (WHERE nullif(btrim(p."Content"),'') IS NULL),
              'maxContentUnicodeCharacters',max(char_length(p."Content")),
              'blankSlug',count(*) FILTER (WHERE nullif(btrim(p."Slug"),'') IS NULL),
              'duplicateNonemptySlugGroups',(SELECT count(*) FROM
                (SELECT "Slug" FROM public."Posts" WHERE nullif(btrim("Slug"),'') IS NOT NULL GROUP BY "Slug" HAVING count(*)>1) dup),
              'structuredLookingContentCandidates',count(*) FILTER (WHERE left(ltrim(p."Content"),1) IN ('{','[')),
              'postsWithCoverUrl',count(*) FILTER (WHERE nullif(btrim(p."ThumbnailUrl"),'') IS NOT NULL),
              'postsWithCoverPublicId',{{identity}}, 'postsWithPositiveCoverDimensions',{{dimensions}},
              'postsWithMarkdownImageCandidate',count(*) FILTER (WHERE
                concat_ws(E'\n',p."Content",p."Summary",p."Excerpt") ~ '!\[[^]]*\]\('),
              'postsWithHtmlImageCandidate',count(*) FILTER (WHERE
                concat_ws(E'\n',p."Content",p."Summary",p."Excerpt") ~* '<img[[:space:]>]'),
              'realVersusSyntheticClassification','requires_owner_confirmation_for_nonempty_data')::text
            FROM public."Posts" p LEFT JOIN public."Categories" c ON c."Id"=p."CategoryId"
            """;
    }

    public const string Categories = """
        SELECT jsonb_build_object('total',count(*),'active',count(*) FILTER (WHERE "IsActive"),
          'blankSlug',count(*) FILTER (WHERE nullif(btrim("Slug"),'') IS NULL),
          'duplicateNonemptySlugGroups',(SELECT count(*) FROM
            (SELECT "Slug" FROM public."Categories" WHERE nullif(btrim("Slug"),'') IS NOT NULL GROUP BY "Slug" HAVING count(*)>1) dup))::text
        FROM public."Categories"
        """;

    public const string Receipts = """
        SELECT jsonb_build_object('total',count(*),'retainedAfterPostDeletion',count(*) FILTER (WHERE "PostId" IS NULL),
          'earliestUtc',min("CreatedAt"),'latestUtc',max("CreatedAt"),
          'externalConsumerIdentity','not_stored_in_receipt_schema')::text FROM public."WebhookReceipts"
        """;

    public const string Migrations = """
        SELECT jsonb_build_object('count',count(*),'latestId',max("MigrationId"))::text
        FROM public."__EFMigrationsHistory"
        """;
}
