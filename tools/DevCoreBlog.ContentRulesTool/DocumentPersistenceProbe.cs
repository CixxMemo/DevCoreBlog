using System.Text.Json;
using DevCoreBlog.Core.Documents;
using DevCoreBlog.Core.Entities;
using DevCoreBlog.Data;
using DevCoreBlog.Data.Repositories;
using DevCoreBlog.Services;
using DevCoreBlog.Services.Documents;
using DevCoreBlog.Services.Publishing;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

/// <summary>Proves document atomicity and races on the shell runner's owned PostgreSQL cluster.</summary>
internal static class DocumentPersistenceProbe
{
    public static async Task<int> RunAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using var db = new ApplicationDbContext(options);
        var connection = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
        var ownedRoot = Environment.GetEnvironmentVariable("DEVCORE_DOCUMENT_FIXTURE_ROOT");
        if (ownedRoot is null || !Path.GetFullPath(ownedRoot).StartsWith("/tmp/devcoreblog-f17.", StringComparison.Ordinal)
            || !File.Exists(Path.Combine(ownedRoot, "postgres", "PG_VERSION"))
            || connection.Host != "127.0.0.1" || connection.Database != "devcoreblog_f01_test"
            || File.ReadAllLines(Path.Combine(ownedRoot, "postgres", "postmaster.pid"))[3] != connection.Port.ToString())
            return 2;
        using var services = new ServiceCollection().AddOptions().AddLogging().AddOutputCache().BuildServiceProvider();
        var cache = new PublicListCacheInvalidator(services.GetRequiredService<IOutputCacheStore>());
        var validator = new ContentDocumentValidator();
        var producer = new DocumentTextProducer();
        var clock = new FixedClock();
        PostDocumentService Service(ApplicationDbContext context) => new(new PostDocumentRepository(context), validator, producer, clock, cache);
        var service = Service(db);
        var checks = new Dictionary<string, bool>();
        const int id = 8001;
        const string json = """
             {"version":1,"document":{"type":"doc","content":[
              {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Türkçe 👋"}]},
              {"type":"paragraph","content":[{"type":"text","text":"  é İstanbul <script> ","marks":[{"type":"bold"}]}]},
              {"type":"codeBlock","attrs":{"language":"csharp"},"content":[{"type":"text","text":"x\n\t y"}]}]}}
            """;
        var row = new Post { Id = id, Title = "F08 synthetic", Slug = "f08-synthetic", Content = "Retained legacy",
            Summary = "Retained summary", Excerpt = "Retained excerpt", CategoryId = 1001, ViewCount = 17,
            IsPublished = true, PublishDate = clock.GetUtcNow().UtcDateTime.AddDays(-1),
            ThumbnailUrl = "https://images.example.test/f08.png", ThumbnailPublicId = "fixture/f08",
            ThumbnailWidth = 640, ThumbnailHeight = 360, ThumbnailAlt = "Retained cover",
            ContentKind = PostContentKind.Experience, CreatedDate = clock.GetUtcNow().UtcDateTime.AddDays(-2) };
        db.Posts.Add(row);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var protectedFacts = Protected(row);
        async Task<Post> Stored() => await db.Posts.AsNoTracking().SingleAsync(p => p.Id == id);
        async Task<string> Snapshot() => JsonSerializer.Serialize(await Stored());
        try
        {
            checks["legacy_and_missing_reads_are_explicit"] = (await service.ReadAsync(id)).Status == PostDocumentReadStatus.Legacy
                && (await service.ReadAsync(int.MaxValue)).Status == PostDocumentReadStatus.NotFound;
            var before = await Snapshot();
            var noIo = new PostDocumentService(new ForbiddenRepository(), validator, producer, clock, cache);
            checks["invalid_document_never_reaches_persistence"] = (await noIo.SaveAsync(id, 1, "{" )).Status == PostDocumentSaveStatus.InvalidDocument;
            checks["invalid_revision_never_reaches_persistence"] = (await noIo.SaveAsync(id, 0, json)).Status == PostDocumentSaveStatus.InvalidInput
                && (await noIo.SaveAsync(id, long.MaxValue, json)).Status == PostDocumentSaveStatus.InvalidInput;
            checks["invalid_document_preserves_every_field"] = (await service.SaveAsync(id, 1, "{\"version\":99}")).Status == PostDocumentSaveStatus.InvalidDocument
                && await Snapshot() == before;
            var saved = await service.SaveAsync(id, 1, json);
            var stored = await Stored();
            var read = await service.ReadAsync(id);
            var validated = validator.Validate(json).Document ?? throw new InvalidOperationException("Fixture invalid.");
            var expected = producer.Produce(validated);
            checks["exact_tiptap_envelope_survives_round_trip"] = saved is { Status: PostDocumentSaveStatus.Saved, EditVersion: 2 }
                && stored.DocumentJson == json && stored.DocumentVersion == 1;
            checks["all_reading_facts_derive_from_same_document"] = read.Status == PostDocumentReadStatus.Ready && read.Document is not null
                && stored.DocumentPlainText == expected.PlainText && stored.DocumentWordCount == expected.WordCount
                && stored.DocumentReadingMinutes == expected.ReadingMinutes && read.Reading?.PlainText == expected.PlainText;
            checks["utc_revision_and_cache_advance_on_commit"] = stored.UpdatedDate == clock.GetUtcNow().UtcDateTime && stored.EditVersion == 2 && cache.Generation == 1;
            checks["all_other_fields_and_counter_are_preserved"] = Protected(stored) == protectedFacts && stored.ViewCount == 17;
            before = await Snapshot();
            checks["stale_save_returns_conflict_without_write_or_eviction"] = (await service.SaveAsync(id, 1, json)).Status == PostDocumentSaveStatus.Conflict
                && await Snapshot() == before && cache.Generation == 1;
            checks["missing_save_is_not_success"] = (await service.SaveAsync(int.MaxValue, 1, json)).Status == PostDocumentSaveStatus.NotFound;

            // Distinct contexts share one start gate; each conditional update competes for revision 2.
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<PostDocumentSaveResult> Compete(string body)
            {
                await using var competitor = new ApplicationDbContext(options);
                await start.Task;
                return await Service(competitor).SaveAsync(id, 2, body);
            }
            var firstJson = json.Replace("Türkçe", "Birinci", StringComparison.Ordinal);
            var secondJson = json.Replace("Türkçe", "İkinci", StringComparison.Ordinal);
            var first = Compete(firstJson); var second = Compete(secondJson);
            start.SetResult();
            var results = await Task.WhenAll(first, second);
            checks["parallel_edit_has_exactly_one_winner"] = results.Count(r => r.Status == PostDocumentSaveStatus.Saved) == 1
                && results.Count(r => r.Status == PostDocumentSaveStatus.Conflict) == 1 && (await Stored()).EditVersion == 3;
            checks["winner_document_and_derivatives_are_consistent"] = (await service.ReadAsync(id)).Status == PostDocumentReadStatus.Ready
                && (await Stored()).DocumentJson == (results[0].Status == PostDocumentSaveStatus.Saved ? firstJson : secondJson);

            async Task Increment()
            {
                await using var counter = new ApplicationDbContext(options);
                await new PostRepository(counter).IncrementVisibleViewCountAsync(id, clock.GetUtcNow().UtcDateTime);
            }
            var counterTasks = Enumerable.Range(0, 10).Select(_ => Increment()).ToArray();
            var counterSave = service.SaveAsync(id, 3, json);
            await Task.WhenAll(counterTasks);
            var counterResult = await counterSave;
            stored = await Stored();
            checks["counter_race_does_not_lose_views_or_document"] = counterResult.Status == PostDocumentSaveStatus.Saved
                && stored.ViewCount == 27 && stored.EditVersion == 4 && stored.DocumentJson == json && Protected(stored) == protectedFacts;

            before = await Snapshot();
            var constraintCases = new PostDocumentWrite[]
            {
                new(2, json, expected.PlainText, expected.WordCount, expected.ReadingMinutes),
                new(1, json, expected.PlainText, -1, 0), new(1, json, expected.PlainText, 1, -1),
                new(1, json, expected.PlainText, 201, 1), new(1, "", "", 0, 0),
                new(1, new string('x', DocumentLimits.Utf8Bytes + 1), "", 0, 0),
                new(1, json, new string('x', DocumentLimits.Utf8Bytes + 1), 0, 0)
            };
            var rejected = true;
            foreach (var bad in constraintCases)
            {
                try { await new PostDocumentRepository(db).TrySaveAsync(id, 4, bad, clock.GetUtcNow().UtcDateTime, default); rejected = false; }
                catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.CheckViolation) { }
                rejected &= await Snapshot() == before;
            }
            checks["database_rejects_invalid_facts_without_partial_update"] = rejected;
            try
            {
                await db.Posts.Where(p => p.Id == id).ExecuteUpdateAsync(setters => setters.SetProperty(p => p.DocumentJson, (string?)null));
                checks["database_rejects_partial_null_set"] = false;
            }
            catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.CheckViolation)
            { checks["database_rejects_partial_null_set"] = await Snapshot() == before; }

            // A constraint-valid but semantically forged derivative must still fail the read trust boundary.
            await db.Posts.Where(p => p.Id == id).ExecuteUpdateAsync(setters => setters.SetProperty(p => p.DocumentPlainText, "forged"));
            checks["inconsistent_storage_is_not_a_trusted_document"] = (await service.ReadAsync(id)) is { Status: PostDocumentReadStatus.Inconsistent, Document: null, Reading: null };
            await service.SaveAsync(id, 4, json);
            var legacy = new PostService(new PostRepository(db), new WebhookPostRepository(db),
                new CategoryRepository(db, NullLogger<CategoryRepository>.Instance),
                new PublicationTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul")), clock, cache);
            before = await Snapshot();
            var legacyInput = new Post { Id = id, Title = "Legacy mutation", Content = "Stale markdown", CategoryId = 1001 };
            checks["legacy_service_cannot_desynchronize_document"] = (await legacy.UpdatePostAsync(legacyInput, expectedEditVersion: 5)).IsConflict
                && await Snapshot() == before;
            checks["legacy_preparation_rejects_before_upload"] = (await legacy.PrepareEditorSaveAsync(legacyInput, PostSaveAction.Save)).IsConflict;
            legacyInput.DocumentJson = json;
            checks["legacy_create_rejects_overposted_document_facts"] = !(await legacy.CreatePostAsync(legacyInput)).IsValid;
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            try { await noIo.SaveAsync(id, 5, json, canceled.Token); checks["cancellation_propagates_before_io"] = false; }
            catch (OperationCanceledException) { checks["cancellation_propagates_before_io"] = true; }
            checks["document_remains_valid_after_rejected_legacy_writes"] = (await service.ReadAsync(id)).Status == PostDocumentReadStatus.Ready;
            var paragraph = "{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"" +
                string.Concat(Enumerable.Repeat("😀", DocumentLimits.TextNodeRunes)) + "\"}]}";
            var largestText = "{\"version\":1,\"document\":{\"type\":\"doc\",\"content\":[" +
                string.Join(',', Enumerable.Repeat(paragraph, 10)) + "]}}";
            var largest = await service.SaveAsync(id, 5, largestText);
            read = await service.ReadAsync(id);
            checks["max_unicode_text_with_added_block_separators_is_persistable"] = largest.Status == PostDocumentSaveStatus.Saved
                && read.Status == PostDocumentReadStatus.Ready && read.Reading is { WordCount: 0, ReadingMinutes: 0 } reading
                && reading.PlainText.EnumerateRunes().Count() == DocumentLimits.TextRunes + 9;
            const string empty = "{\"version\":1,\"document\":{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\"}]}}";
            var byteBoundary = new string(' ', DocumentLimits.Utf8Bytes - empty.Length) + empty;
            checks["one_megabyte_json_and_zero_reading_facts_round_trip"] = (await service.SaveAsync(id, 6, byteBoundary)).Status == PostDocumentSaveStatus.Saved
                && (await service.ReadAsync(id)) is { Status: PostDocumentReadStatus.Ready, Reading: { PlainText: "", WordCount: 0, ReadingMinutes: 0 } };
            Console.WriteLine(JsonSerializer.Serialize(new { count = checks.Count, checks }));
            var report = Environment.GetEnvironmentVariable("DEVCORE_VOL1_F08_REPORT_DIR");
            if (!string.IsNullOrEmpty(report)) await File.WriteAllTextAsync(Path.Combine(report, "document-persistence.json"),
                JsonSerializer.Serialize(new { count = checks.Count, checks }, new JsonSerializerOptions { WriteIndented = true }));
            return checks.Values.All(v => v) ? 0 : 1;
        }
        finally { await db.Posts.Where(p => p.Id == id).ExecuteDeleteAsync(); }
    }

    private static string Protected(Post post) => JsonSerializer.Serialize(new { post.Id, post.Title, post.Slug, post.Summary,
        post.Content, post.Excerpt, post.CategoryId, post.CreatedDate, post.IsActive, post.IsPublished, post.PublishDate,
        post.ContentKind, post.AccessScope, post.ThumbnailUrl, post.ThumbnailPublicId, post.ThumbnailWidth, post.ThumbnailHeight, post.ThumbnailAlt });

    private sealed class FixedClock : TimeProvider
    { public override DateTimeOffset GetUtcNow() => new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero); }

    private sealed class ForbiddenRepository : IPostDocumentRepository
    {
        public Task<StoredPostDocument?> FindAsync(int postId, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected I/O.");
        public Task<DocumentWriteOutcome> TrySaveAsync(int postId, long expectedEditVersion, PostDocumentWrite document,
            DateTime updatedUtc, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected I/O.");
    }
}
