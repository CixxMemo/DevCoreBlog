using System.Text.Json;
using DevCoreBlog.Core.Entities;
using DevCoreBlog.Core.Interfaces;
using DevCoreBlog.Core.Newsletters;
using DevCoreBlog.Data;
using DevCoreBlog.Data.Newsletters;
using DevCoreBlog.Data.Repositories;
using DevCoreBlog.Services.Documents;
using DevCoreBlog.Services.Email;
using DevCoreBlog.Services.Newsletters;
using Microsoft.EntityFrameworkCore;
using Npgsql;

/// <summary>Owned PostgreSQL only: real snapshot constraints, parallel starts and source/cancellation races. No transport exists.</summary>
internal static class NewsletterCampaignProbe
{
    public static async Task<int> RunAsync(DbContextOptions<ApplicationDbContext> options, bool seed)
    {
        await using var db = new ApplicationDbContext(options);
        var connection = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
        var owned = Environment.GetEnvironmentVariable("CAMPAIGN_TEST_ROOT");
        if (owned is null || !Path.GetFullPath(owned).StartsWith("/tmp/devcoreblog-campaign-", StringComparison.Ordinal)
            || !File.Exists(Path.Combine(owned, "pg", "PG_VERSION")) || connection.Host != "127.0.0.1"
            || connection.Database is null || !connection.Database.StartsWith("campaign_", StringComparison.Ordinal)
            || File.ReadAllLines(Path.Combine(owned, "pg", "postmaster.pid"))[3] != connection.Port.ToString()) return 2;
        var now = DateTime.UtcNow; now = now.AddTicks(-(now.Ticks % 10));
        var validator = new ContentDocumentValidator(); var producer = new DocumentTextProducer(); var renderer = new DocumentEmailRenderer(producer);
        var profile = new ResendOptions("re_synthetic_campaign", "sender@example.test", "Sentetik Aether", "reply@example.test");
        var origin = new NewsletterCampaignOrigin("https://aether.example.test"); var clock = new Clock(now);
        var store = new NewsletterCampaignStore(db);
        NewsletterCampaignService Service(INewsletterCampaignStore repository, ResendOptions? settings = null, NewsletterCampaignOrigin? web = null) =>
            new(repository, settings ?? profile, web ?? origin, validator, producer, renderer, clock);
        var service = Service(store); var checks = new Dictionary<string, bool>();
        var category = new Category { Name = "Campaign fixture", Slug = "campaign-" + Guid.NewGuid().ToString("N"), CreatedDate = now, IsActive = true };
        db.Categories.Add(category); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        string Document(string value) => JsonSerializer.Serialize(new { version = 1, document = new { type = "doc", content = new[] {
            new { type = "paragraph", content = new[] { new { type = "text", text = value } } } } } });
        async Task<Post> Source(string value = "Sentetik bülten <script> 👋")
        {
            var json = Document(value); var validated = validator.Validate(json).Document ?? throw new InvalidOperationException(); var reading = producer.Produce(validated);
            var post = new Post { Title = "Sentetik konu", Slug = "campaign-" + Guid.NewGuid().ToString("N"), CategoryId = category.Id,
                Content = "Retained synthetic legacy", Summary = "Sentetik özet", Excerpt = "", ContentKind = PostContentKind.Newsletter,
                AccessScope = PostAccessScope.Subscribers, IsActive = true, IsPublished = false, PublishDate = now.AddDays(30), CreatedDate = now,
                DocumentVersion = 1, DocumentJson = json, DocumentPlainText = reading.PlainText, DocumentWordCount = reading.WordCount, DocumentReadingMinutes = reading.ReadingMinutes };
            db.Posts.Add(post); await db.SaveChangesAsync(); db.ChangeTracker.Clear(); return post;
        }
        Task<NewsletterCampaign> Frozen(Guid id) => db.NewsletterCampaigns.AsNoTracking().SingleAsync(x => x.Id == id);
        Task<CampaignStartResult> Start(Post post, Guid key) => service.PrepareAsync(post.Id, post.EditVersion, key, default);
        var source = await Source(); var key = Guid.NewGuid();
        var beforeJobs = await db.TransactionalEmailJobs.CountAsync(); var beforeAttempts = await db.TransactionalEmailAttempts.CountAsync();
        checks["missing_sender_does_not_create_intent"] = (await Service(store, new ResendOptions(null, null, null)).PrepareAsync(source.Id, 1, key, default)).Status == CampaignStartStatus.NotConfigured;
        checks["non_https_origin_does_not_create_intent"] = (await Service(store, web: new("http://127.0.0.1")).PrepareAsync(source.Id, 1, key, default)).Status == CampaignStartStatus.NotConfigured;
        checks["invalid_and_stale_request_do_not_consume_key"] = (await service.PrepareAsync(source.Id, 0, key, default)).Status == CampaignStartStatus.Invalid
            && (await service.PrepareAsync(source.Id, 2, key, default)).Status == CampaignStartStatus.SourceChanged && await store.FindIntentAsync(key, default) is null;
        var created = await Start(source, key); var intent = created.Intent ?? throw new InvalidOperationException(); var original = await Frozen(intent.Id);
        checks["draft_campaign_prepared_without_publishing"] = created.Status == CampaignStartStatus.Created && original.State == NewsletterCampaignState.Prepared
            && original.SourceRevision == 1 && !await db.Posts.Where(x => x.Id == source.Id).Select(x => x.IsPublished).SingleAsync();
        checks["canonical_content_and_render_are_frozen"] = original.DocumentJson == source.DocumentJson && original.DocumentPlainText == source.DocumentPlainText
            && original.Html.Contains("&lt;script&gt;", StringComparison.Ordinal) && !original.Html.Contains("<script", StringComparison.Ordinal)
            && original.Text.Contains("Sentetik bülten", StringComparison.Ordinal) && original.RenderVersion == 1 && original.DocumentVersion == 1;
        checks["sender_and_origin_are_frozen_without_credentials"] = original.SenderEmail == "sender@example.test" && original.SenderName == "Sentetik Aether"
            && original.ReplyTo == "reply@example.test" && original.ProviderContract == "resend/v1"
            && original.WebVersionUrl == "https://aether.example.test/post/" + source.Slug
            && !JsonSerializer.Serialize(original).Contains("re_synthetic_campaign", StringComparison.Ordinal)
            && !JsonSerializer.Serialize(original).Contains(profile.Binding ?? "missing", StringComparison.Ordinal);
        if (seed)
        {
            var file = Environment.GetEnvironmentVariable("CAMPAIGN_TEST_SEED_FILE") ?? throw new InvalidOperationException();
            var ordinary = await Source("Ordinary fixture");
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(new { postId = source.Id, ordinaryId = ordinary.Id, campaignId = intent.Id }));
            return Report(checks);
        }
        var originalJson = JsonSerializer.Serialize(original);
        checks["same_start_key_returns_same_identity"] = (await Start(source, key)).Intent?.Id == intent.Id && await db.NewsletterCampaigns.CountAsync(x => x.StartKey == key) == 1;
        await db.Posts.Where(x => x.Id == source.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Title, "Yeni konu").SetProperty(x => x.EditVersion, 2L));
        checks["editor_change_does_not_mutate_snapshot"] = originalJson == JsonSerializer.Serialize(await Frozen(intent.Id));
        checks["original_intent_replays_after_edit_and_config_rotation"] = (await Service(store, new("re_rotated", "other@example.test", "Other")).PrepareAsync(source.Id, 1, key, default)).Intent?.Id == intent.Id;
        checks["new_revision_same_key_conflicts"] = (await service.PrepareAsync(source.Id, 2, key, default)).Status == CampaignStartStatus.Conflict;
        var other = await Source(); checks["different_source_same_key_conflicts"] = (await Start(other, key)).Status == CampaignStartStatus.Conflict;
        var updated = await service.PrepareAsync(source.Id, 2, Guid.NewGuid(), default);
        checks["explicit_new_intent_freezes_new_revision"] = updated.Status == CampaignStartStatus.Created && updated.Intent?.Id != intent.Id
            && (await Frozen(updated.Intent?.Id ?? Guid.Empty)).Subject == "Yeni konu" && originalJson == JsonSerializer.Serialize(await Frozen(intent.Id));
        var article = await Source(); await db.Posts.Where(x => x.Id == article.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ContentKind, PostContentKind.Experience));
        checks["non_newsletter_source_rejected"] = (await Start(article, Guid.NewGuid())).Status == CampaignStartStatus.NotFound;
        var corrupt = await Source(); await db.Posts.Where(x => x.Id == corrupt.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.DocumentPlainText, "Inconsistent"));
        checks["inconsistent_derived_facts_rejected"] = (await Start(corrupt, Guid.NewGuid())).Status == CampaignStartStatus.Invalid;
        var raced = await Source(); var raceKey = Guid.NewGuid();
        async Task<CampaignStartResult> Concurrent(Post post, Guid startKey)
        {
            await using var ctx = new ApplicationDbContext(options); return await Service(new NewsletterCampaignStore(ctx)).PrepareAsync(post.Id, post.EditVersion, startKey, default);
        }
        var starts = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Concurrent(raced, raceKey)));
        checks["twelve_parallel_starts_one_campaign"] = starts.Count(x => x.Status == CampaignStartStatus.Created) == 1
            && starts.Count(x => x.Status == CampaignStartStatus.Existing) == 11 && starts.Select(x => x.Intent?.Id).Distinct().Count() == 1;
        var conflictKey = Guid.NewGuid(); var conflicts = await Task.WhenAll(Concurrent(raced, conflictKey), Concurrent(other, conflictKey));
        checks["parallel_key_payload_conflict_never_creates_second_campaign"] = conflicts.Count(x => x.Status == CampaignStartStatus.Created) == 1
            && conflicts.Count(x => x.Status == CampaignStartStatus.Conflict) == 1 && await db.NewsletterCampaigns.CountAsync(x => x.StartKey == conflictKey) == 1;
        async Task<bool> Reject(FormattableString sql, string code, string? message = null)
        {
            try { await db.Database.ExecuteSqlInterpolatedAsync(sql); return false; }
            catch (PostgresException error) { return error.SqlState == code && (message is null || error.MessageText.Contains(message, StringComparison.Ordinal)); }
        }
        FormattableString[] mutations = [
            $"UPDATE \"NewsletterCampaigns\" SET \"Subject\"={"tampered"} WHERE \"Id\"={intent.Id}",
            $"UPDATE \"NewsletterCampaigns\" SET \"DocumentJson\"={"tampered"} WHERE \"Id\"={intent.Id}",
            $"UPDATE \"NewsletterCampaigns\" SET \"Html\"={"tampered"} WHERE \"Id\"={intent.Id}",
            $"UPDATE \"NewsletterCampaigns\" SET \"Text\"={"tampered"} WHERE \"Id\"={intent.Id}",
            $"UPDATE \"NewsletterCampaigns\" SET \"SenderEmail\"={"tampered@example.test"} WHERE \"Id\"={intent.Id}",
            $"UPDATE \"NewsletterCampaigns\" SET \"SenderName\"={"tampered"} WHERE \"Id\"={intent.Id}",
            $"UPDATE \"NewsletterCampaigns\" SET \"ReplyTo\"={"tampered@example.test"} WHERE \"Id\"={intent.Id}",
            $"UPDATE \"NewsletterCampaigns\" SET \"ProviderContract\"={"other/v1"} WHERE \"Id\"={intent.Id}",
            $"UPDATE \"NewsletterCampaigns\" SET \"WebVersionUrl\"={"https://other.example.test/"} WHERE \"Id\"={intent.Id}",
            $"UPDATE \"NewsletterCampaigns\" SET \"SnapshotHash\"={new string('A',64)} WHERE \"Id\"={intent.Id}"
        ];
        var blocked = true;
        foreach (var mutation in mutations) blocked &= await Reject(mutation, "23514", "immutable");
        checks["database_rejects_snapshot_mutation_including_valid_html"] = blocked && originalJson == JsonSerializer.Serialize(await Frozen(intent.Id));
        // Demonstrate that the guard, rather than coincidental field constraints, blocks a valid-looking change.
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"NewsletterCampaigns\" DISABLE TRIGGER \"TR_NewsletterCampaigns_Immutable\"");
            await db.NewsletterCampaigns.Where(x => x.Id == intent.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Subject, "Guard counterexample"));
            checks["negative_control_detects_missing_immutability_guard"] = (await Frozen(intent.Id)).Subject == "Guard counterexample";
            await tx.RollbackAsync();
        }
        checks["guard_negative_control_rolls_back_all_changes"] = originalJson == JsonSerializer.Serialize(await Frozen(intent.Id))
            && await Reject($"UPDATE \"NewsletterCampaigns\" SET \"Subject\"={"Guard counterexample"} WHERE \"Id\"={intent.Id}", "23514", "immutable");
        checks["database_retains_campaign_history"] = await Reject($"DELETE FROM \"NewsletterCampaigns\" WHERE \"Id\"={intent.Id}", "23514", "retained");
        checks["database_enforces_snapshot_body_limit_on_insert"] = await Reject($"""
            INSERT INTO "NewsletterCampaigns"
            SELECT (jsonb_populate_record(NULL::"NewsletterCampaigns",to_jsonb(c)||jsonb_build_object(
                'Id',{Guid.NewGuid()},'StartKey',{Guid.NewGuid()},'Html',{new string('x', NewsletterCampaign.MaximumHtmlBytes + 1)}))).*
            FROM "NewsletterCampaigns" c WHERE c."Id"={intent.Id}
            """, "23514");
        checks["source_delete_is_domain_conflict"] = await new PostRepository(db).TryDeleteAsync(source.Id) == PostDeleteOutcome.Referenced && await db.Posts.AnyAsync(x => x.Id == source.Id);
        var ordinaryPost = await Source(); checks["unreferenced_delete_and_missing_delete_keep_semantics"] = await new PostRepository(db).TryDeleteAsync(ordinaryPost.Id) == PostDeleteOutcome.Deleted
            && await new PostRepository(db).TryDeleteAsync(ordinaryPost.Id) == PostDeleteOutcome.NotFound;
        checks["stale_cancel_and_backwards_clock_do_not_mutate"] = await service.CancelAsync(intent.Id, 99, default) == CampaignCancelStatus.Conflict
            && await store.TryCancelAsync(intent.Id, 1, now.AddSeconds(-1), default) == CampaignCancelStatus.InvalidTime && originalJson == JsonSerializer.Serialize(await Frozen(intent.Id));
        async Task<CampaignCancelStatus> Cancel()
        {
            await using var ctx = new ApplicationDbContext(options); return await Service(new NewsletterCampaignStore(ctx)).CancelAsync(intent.Id, 1, default);
        }
        var cancelled = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Cancel())); var afterCancel = await Frozen(intent.Id);
        checks["parallel_cancel_one_transition"] = cancelled.Count(x => x == CampaignCancelStatus.Cancelled) == 1 && cancelled.Count(x => x == CampaignCancelStatus.Unchanged) == 11
            && afterCancel.State == NewsletterCampaignState.Cancelled && afterCancel.Version == 2 && afterCancel.SnapshotHash == original.SnapshotHash;
        checks["cancelled_start_key_never_reactivates"] = (await Start(source, key)).Intent is { State: NewsletterCampaignState.Cancelled, Version: 2 };
        checks["database_rejects_terminal_reactivation"] = await Reject($"UPDATE \"NewsletterCampaigns\" SET \"State\"=0,\"Version\"=1 WHERE \"Id\"={intent.Id}", "23514", "transition");
        await using (var restarted = new ApplicationDbContext(options))
            checks["restart_preserves_intent_and_frozen_hash"] = (await Service(new NewsletterCampaignStore(restarted)).PrepareAsync(source.Id, 1, key, default)).Intent?.SnapshotHash == original.SnapshotHash;
        var stale = await Source(); var pausing = new PausingStore(store); var staleKey = Guid.NewGuid();
        var preparation = Service(pausing).PrepareAsync(stale.Id, 1, staleKey, default);
        await pausing.Read.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await db.Posts.Where(x => x.Id == stale.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Title, "Concurrent edit").SetProperty(x => x.EditVersion, 2L));
        pausing.Continue.SetResult();
        checks["edit_between_read_and_commit_rejected_without_consuming_key"] = (await preparation).Status == CampaignStartStatus.SourceChanged && await store.FindIntentAsync(staleKey, default) is null;
        var categoryRace = await Source(); var categoryPause = new PausingStore(store); var categoryKey = Guid.NewGuid();
        var categoryPreparation = Service(categoryPause).PrepareAsync(categoryRace.Id, 1, categoryKey, default);
        await categoryPause.Read.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await db.Categories.Where(x => x.Id == category.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        categoryPause.Continue.SetResult();
        checks["category_disabled_before_commit_rejected"] = (await categoryPreparation).Status == CampaignStartStatus.NotFound && await store.FindIntentAsync(categoryKey, default) is null;
        await db.Categories.Where(x => x.Id == category.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, true));
        var lockedSource = await Source();
        foreach (var check in await SourceLockProbeAsync(options, lockedSource, repository => Service(repository))) checks.Add(check.Key, check.Value);
        checks["no_transactional_mail_or_attempts_created"] = await db.TransactionalEmailJobs.CountAsync() == beforeJobs && await db.TransactionalEmailAttempts.CountAsync() == beforeAttempts;
        return Report(checks);
    }
    private static int Report(Dictionary<string, bool> checks)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { passed = checks.Values.All(x => x), count = checks.Count, checks })); return checks.Values.All(x => x) ? 0 : 1;
    }
    private sealed class Clock(DateTime now) : TimeProvider { public override DateTimeOffset GetUtcNow() => new(now); }
    private static async Task<Dictionary<string, bool>> SourceLockProbeAsync(DbContextOptions<ApplicationDbContext> options,
        Post source, Func<INewsletterCampaignStore, NewsletterCampaignService> service)
    {
        await using var db = new ApplicationDbContext(options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION "CampaignFixtureGate"() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN PERFORM pg_advisory_xact_lock(734341); RETURN NEW; END $$;
            CREATE TRIGGER "TR_CampaignFixtureGate" BEFORE INSERT ON "NewsletterCampaigns"
                FOR EACH ROW EXECUTE FUNCTION "CampaignFixtureGate"();
            """);
        await using var monitor = new NpgsqlConnection(db.Database.GetConnectionString()); await monitor.OpenAsync();
        await using var blocker = new NpgsqlConnection(db.Database.GetConnectionString()); await blocker.OpenAsync();
        await using var tx = await blocker.BeginTransactionAsync();
        await using var hold = new NpgsqlCommand("SELECT pg_advisory_xact_lock(734341)", blocker, tx); await hold.ExecuteNonQueryAsync();
        async Task<bool> WaitFor(string sql, int minimum)
        {
            for (var i = 0; i < 100; i++)
            {
                await using var query = new NpgsqlCommand(sql, monitor);
                if (Convert.ToInt64(await query.ExecuteScalarAsync()) >= minimum) return true;
                await Task.Delay(25);
            }
            return false;
        }
        async Task<CampaignStartResult> Prepare()
        {
            await using var ctx = new ApplicationDbContext(options);
            return await service(new NewsletterCampaignStore(ctx)).PrepareAsync(source.Id, 1, Guid.NewGuid(), default);
        }
        async Task EditSource()
        {
            await using var ctx = new ApplicationDbContext(options);
            await ctx.Posts.Where(x => x.Id == source.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Title, "After commit").SetProperty(x => x.EditVersion, 2L));
        }
        async Task DisableCategory()
        {
            await using var ctx = new ApplicationDbContext(options);
            await ctx.Categories.Where(x => x.Id == source.CategoryId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        }
        var released = false;
        try
        {
            var preparation = Prepare();
            if (!await WaitFor("SELECT count(*) FROM pg_stat_activity WHERE datname=current_database() AND wait_event='advisory'", 1))
                throw new InvalidOperationException("Source lock fixture gate unavailable.");
            var edit = EditSource(); var category = DisableCategory();
            var blocked = await WaitFor("SELECT count(*) FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock' AND wait_event<>'advisory' AND state='active'", 2);
            await tx.CommitAsync(); released = true; await Task.WhenAll(preparation, edit, category);
            var result = await preparation; var saved = await db.NewsletterCampaigns.AsNoTracking().SingleAsync(x => x.Id == (result.Intent == null ? Guid.Empty : result.Intent.Id));
            return new()
            {
                ["source_and_category_edits_wait_for_snapshot_commit"] = blocked,
                ["commit_uses_single_pre_edit_source_revision"] = result.Status == CampaignStartStatus.Created && saved.SourceRevision == 1
                    && saved.Subject == source.Title && await db.Posts.AnyAsync(x => x.Id == source.Id && x.EditVersion == 2)
            };
        }
        finally
        {
            if (!released) await tx.RollbackAsync();
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER \"TR_CampaignFixtureGate\" ON \"NewsletterCampaigns\"; DROP FUNCTION \"CampaignFixtureGate\"();");
        }
    }
    private sealed class PausingStore(INewsletterCampaignStore store) : INewsletterCampaignStore
    {
        public TaskCompletionSource Read { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<CampaignSource?> ReadSourceAsync(int postId, CancellationToken cancellationToken)
        { var source = await store.ReadSourceAsync(postId, cancellationToken); Read.SetResult(); return source; }
        public Task<CampaignIntent?> FindIntentAsync(Guid key, CancellationToken cancellationToken) => store.FindIntentAsync(key, cancellationToken);
        public async Task<CampaignStartResult> TryCreateAsync(NewsletterCampaign campaign, CancellationToken cancellationToken)
        { await Continue.Task.WaitAsync(cancellationToken); return await store.TryCreateAsync(campaign, cancellationToken); }
        public Task<CampaignCancelStatus> TryCancelAsync(Guid id, long version, DateTime time, CancellationToken cancellationToken) => store.TryCancelAsync(id, version, time, cancellationToken);
    }
}
