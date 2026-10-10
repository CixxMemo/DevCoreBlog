using DevCoreBlog.Core.Entities;
using DevCoreBlog.Core.Publishing;
using DevCoreBlog.Core.Validation;
using DevCoreBlog.Data;
using DevCoreBlog.Data.Repositories;
using DevCoreBlog.Services;
using DevCoreBlog.Services.Publishing;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

/// <summary>Exercises access rules through real PostgreSQL repositories and service writes.</summary>
internal static class AccessProbe
{
    public static async Task<int> RunAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using var context = new ApplicationDbContext(options);
        // The shell runner owns this named disposable database; never run on application data.
        if (context.Database.GetDbConnection().Database != "devcoreblog_f01_test") return 2;
        var now = DateTime.UtcNow;
        var repository = new PostRepository(context);
        var categories = new CategoryRepository(context, NullLogger<CategoryRepository>.Instance);
        using var services = new ServiceCollection().AddOptions().AddLogging().AddOutputCache().BuildServiceProvider();
        var cache = new PublicListCacheInvalidator(services.GetRequiredService<IOutputCacheStore>());
        var service = new PostService(repository, new WebhookPostRepository(context), categories,
            new PublicationTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul")), TimeProvider.System, cache);
        var checks = new Dictionary<string, bool>();

        Post Fixture(int id, string slug, PostContentKind kind, PostAccessScope access) => new()
        {
            Id = id, Slug = slug, Title = $"F04_PRIVATE_TITLE_{id}", Summary = $"F04_PRIVATE_SUMMARY_{id}",
            Content = $"F04_PRIVATE_BODY_{id}", Excerpt = $"F04_PRIVATE_EXCERPT_{id}",
            ThumbnailUrl = $"https://images.example.test/f04-private-cover-{id}.png",
            CategoryId = 1001, IsPublished = true, PublishDate = now.AddDays(-1),
            ContentKind = kind, AccessScope = access, ViewCount = 9000
        };
        var newsletter = Fixture(4001, "f04-private-newsletter", PostContentKind.Newsletter, PostAccessScope.Subscribers);
        var article = Fixture(4002, "f04-private-article", PostContentKind.Experience, PostAccessScope.Subscribers);
        var scheduled = Fixture(4003, "f04-private-scheduled", PostContentKind.Newsletter, PostAccessScope.Subscribers);
        scheduled.PublishDate = now.AddMinutes(1);
        var draft = Fixture(4004, "f04-private-draft", PostContentKind.Newsletter, PostAccessScope.Subscribers);
        draft.IsPublished = false;
        var inactive = Fixture(4005, "f04-private-inactive", PostContentKind.Newsletter, PostAccessScope.Subscribers);
        inactive.IsActive = false;
        var control = Fixture(4010, "f04-public-control", PostContentKind.AiNews, PostAccessScope.Public);
        control.Title = control.Summary = control.Content = control.Excerpt = "F04_PUBLIC_CONTROL";
        control.ThumbnailUrl = string.Empty;
        context.Posts.AddRange(newsletter, article, scheduled, draft, inactive, control);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var privateIds = new HashSet<int> { 4001, 4002, 4003, 4004, 4005 };
        var privateSlugs = new HashSet<string> { newsletter.Slug, article.Slug, scheduled.Slug, draft.Slug, inactive.Slug };

        async Task<bool> PublicMatrix()
        {
            var legacyList = await service.GetPublishedPostsAsync();
            var legacyCategory = await service.GetPostsByCategorySlugAsync("f01-active");
            var list = await service.GetPublishedPostsPagedAsync(1, 27);
            var category = await service.GetPostsByCategorySlugPagedAsync("f01-active", 1, 27);
            var search = await service.SearchPostsPagedAsync("F04_PRIVATE", null, 1, 27);
            var related = await service.GetRelatedPostsAsync(2001, 1001);
            var top = await service.GetMostReadPublicPostsAsync();
            var feed = await service.GetLatestPublicPostsAsync();
            var sitemap = await service.GetSitemapPostsAsync();
            var rss = await service.GetRssPostsAsync();
            IEnumerable<int>[] lists = [legacyList.Select(p => p.Id), legacyCategory.Select(p => p.Id),
                list.Posts.Select(p => p.Id), category.Posts.Select(p => p.Id), search.Posts.Select(p => p.Id),
                related.Select(p => p.Id), top.Select(p => p.Id), feed.Select(p => p.Id)];
            return lists.All(rows => rows.All(id => !privateIds.Contains(id))) && search.TotalCount == 0 &&
                sitemap.All(p => !privateSlugs.Contains(p.Slug)) && rss.All(p => !privateSlugs.Contains(p.Slug)) &&
                list.Posts.Any(p => p.Id == control.Id) && feed.Any(p => p.Id == control.Id);
        }
        checks["all_public_repository_surfaces_exclude_private_with_positive_control"] = await PublicMatrix();
        checks["private_details_and_counter_are_denied"] =
            await service.GetPostBySlugAsync(newsletter.Slug) is null &&
            await service.GetPostBySlugAsync(article.Slug) is null &&
            await service.IncrementViewCountAsync(newsletter.Id) is null &&
            await context.Posts.Where(p => p.Id == newsletter.Id).Select(p => p.ViewCount).SingleAsync() == 9000;
        checks["private_schedule_does_not_bound_public_cache"] =
            await service.GetNextScheduledPublicationAsync(now) ==
            await context.Posts.Where(p => p.Id == 2002).Select(p => (DateTime?)p.PublishDate).SingleAsync();
        checks["admin_reads_private_and_publication_state_stays_independent"] =
            (await service.GetAllPostsAsync()).Count(p => privateIds.Contains(p.Id)) == privateIds.Count &&
            PostPublication.StateAt(newsletter, true, now) == PostPublicationState.Published;

        var count = await context.Posts.CountAsync();
        var invalidInputs = new[]
        {
            Fixture(0, "", PostContentKind.Newsletter, PostAccessScope.Public),
            Fixture(0, "", (PostContentKind)99, PostAccessScope.Public),
            Fixture(0, "", PostContentKind.AiNews, (PostAccessScope)99)
        };
        var rejected = true;
        foreach (var invalid in invalidInputs)
            rejected &= !(await service.CreatePostAsync(invalid)).IsValid;
        checks["service_rejects_unknown_values_and_public_newsletter_without_writes"] =
            rejected && await context.Posts.CountAsync() == count;
        checks["domain_accepts_each_defined_kind_and_allowed_access"] =
            Enum.GetValues<PostContentKind>().All(kind => PostAccessRules.Validate(
                Fixture(0, "", kind, PostAccessScope.Subscribers)).IsValid &&
                (kind == PostContentKind.Newsletter || PostAccessRules.Validate(
                    Fixture(0, "", kind, PostAccessScope.Public)).IsValid));
        var visible = PostPublication.VisibleAt(now).Compile();
        checks["domain_visibility_fails_closed_for_corrupt_values"] = invalidInputs.All(post =>
        {
            post.Category = new Category { IsActive = true };
            return !visible(post);
        });

        // Fixed owned-fixture statements exercise the database independently of service validation.
        var constraintsHold = true;
        foreach (var sql in new[] {
            "UPDATE \"Posts\" SET \"ContentKind\"=99 WHERE \"Id\"=4010",
            "UPDATE \"Posts\" SET \"AccessScope\"=99 WHERE \"Id\"=4010",
            "UPDATE \"Posts\" SET \"ContentKind\"=4, \"AccessScope\"=0 WHERE \"Id\"=4010" })
        {
            try { await context.Database.ExecuteSqlRawAsync(sql); constraintsHold = false; }
            catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.CheckViolation) { }
        }
        checks["database_rejects_each_invalid_metadata_case"] = constraintsHold;

        // A rollback-only transaction proves SQL queries still fail closed if a check was bypassed.
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            await context.Database.ExecuteSqlRawAsync("ALTER TABLE \"Posts\" DROP CONSTRAINT \"CK_Posts_NewsletterAccess\", DROP CONSTRAINT \"CK_Posts_ContentKind\", DROP CONSTRAINT \"CK_Posts_AccessScope\"");
            await context.Database.ExecuteSqlRawAsync("UPDATE \"Posts\" SET \"AccessScope\"=0 WHERE \"Id\"=4001; UPDATE \"Posts\" SET \"ContentKind\"=99, \"AccessScope\"=0 WHERE \"Id\"=4002; UPDATE \"Posts\" SET \"AccessScope\"=99 WHERE \"Id\"=4003");
            checks["sql_public_surfaces_fail_closed_when_constraints_are_bypassed"] = await PublicMatrix() &&
                await service.IncrementViewCountAsync(newsletter.Id) is null;
            await transaction.RollbackAsync();
        }
        context.ChangeTracker.Clear();
        var edit = await context.Posts.AsNoTracking().SingleAsync(p => p.Id == control.Id);
        var generation = cache.Generation;
        var version = edit.EditVersion;
        edit.AccessScope = PostAccessScope.Subscribers;
        var saved = await service.UpdatePostAsync(edit, expectedEditVersion: version);
        var committed = await context.Posts.AsNoTracking().SingleAsync(p => p.Id == control.Id);
        checks["access_edit_is_versioned_and_invalidates_public_cache"] = saved.IsValid &&
            cache.Generation == generation + 1 && committed.EditVersion == version + 1 && committed.UpdatedDate.HasValue &&
            await service.GetPostBySlugAsync(control.Slug) is null;
        // Restore only this synthetic positive control for the subsequent HTTP cache test.
        context.ChangeTracker.Clear();
        edit = await context.Posts.AsNoTracking().SingleAsync(p => p.Id == control.Id);
        edit.AccessScope = PostAccessScope.Public;
        checks["compatible_forward_edit_restores_only_explicit_public_control"] =
            (await service.UpdatePostAsync(edit, expectedEditVersion: edit.EditVersion)).IsValid &&
            await service.GetPostBySlugAsync(control.Slug) is not null;
        foreach (var check in checks) Console.WriteLine($"f04_{check.Key}={check.Value.ToString().ToLowerInvariant()}");
        return checks.Values.All(value => value) ? 0 : 1;
    }
}
