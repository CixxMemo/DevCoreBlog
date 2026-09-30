using DevCoreBlog.Core.Entities;
using DevCoreBlog.Data;
using DevCoreBlog.Data.Repositories;
using DevCoreBlog.Services;
using DevCoreBlog.Services.Publishing;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

internal static class VisibilityProbe
{
    // Exercise the real PostgreSQL query through a controllable service clock.
    public static async Task<int> RunAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using var context = new ApplicationDbContext(options);
        var postRepository = new PostRepository(context);
        var categoryRepository = new CategoryRepository(
            context, NullLogger<CategoryRepository>.Instance);
        var clock = new FixedTimeProvider(TimeProvider.System.GetUtcNow());
        using var cacheServices = new ServiceCollection()
            .AddOptions()
            .AddLogging()
            .AddOutputCache()
            .BuildServiceProvider();
        var publicListCache = new PublicListCacheInvalidator(
            cacheServices.GetRequiredService<IOutputCacheStore>());
        var service = new PostService(
            postRepository,
            new WebhookPostRepository(context),
            categoryRepository,
            new PublicationTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul")),
            clock,
            publicListCache);

        var listed = (await service.GetPublishedPostsAsync()).Select(post => post.Id).ToHashSet();
        var paged = await service.GetPublishedPostsPagedAsync(1, 9);
        var categoryPosts = await service.GetPostsByCategorySlugAsync("f01-active");
        var categoryPaged = await service.GetPostsByCategorySlugPagedAsync("f01-active", 1, 9);
        var search = await service.SearchPostsAsync("F01_");
        var related = await service.GetRelatedPostsAsync(2001, 1001);
        var latest = await service.GetLatestPublicPostsAsync();
        var admin = (await service.GetAllPostsAsync()).Select(post => post.Id).ToHashSet();
        var activeCategories = await categoryRepository.GetActiveCategoriesAsync();

        var futureDate = await context.Posts.AsNoTracking()
            .Where(post => post.Id == 2002)
            .Select(post => post.PublishDate)
            .SingleAsync();
        clock.Set(new DateTimeOffset(futureDate.AddTicks(-10), TimeSpan.Zero));
        var futureBefore = await service.GetPostBySlugAsync("f01-future-visible-marker");
        clock.Set(new DateTimeOffset(futureDate, TimeSpan.Zero));
        var futureAt = await service.GetPostBySlugAsync("f01-future-visible-marker");

        var hidden = new[] { 2002, 2003, 2004, 2008 };
        var publicLists = new IEnumerable<Post>[]
        {
            paged.Posts, categoryPosts, categoryPaged.Posts, search, related, latest
        };
        var checks = new Dictionary<string, bool>
        {
            ["public_unpaged_visibility"] = listed.Contains(2001) &&
                hidden.All(id => !listed.Contains(id)),
            ["every_public_query_hides_ineligible_rows"] =
                publicLists.All(posts => hidden.All(id => posts.All(post => post.Id != id))),
            ["public_category_and_feed_are_bounded"] =
                (await categoryRepository.GetActiveCategoryBySlugAsync("f01-inactive")) == null &&
                activeCategories.All(category => category.IsActive) &&
                latest.Count() <= 3,
            ["admin_keeps_hidden_rows"] = hidden.All(admin.Contains),
            ["boundary_before_is_hidden"] = futureBefore == null,
            ["boundary_at_is_visible"] = futureAt?.Id == 2002
        };

        foreach (var check in checks)
        {
            Console.WriteLine($"f17_{check.Key}={check.Value.ToString().ToLowerInvariant()}");
        }

        Console.WriteLine($"f17_inactive_category_post_count={await context.Posts.CountAsync(
            post => post.CategoryId == 1002)}");
        return checks.Values.All(passed => passed) ? 0 : 1;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Set(DateTimeOffset value) => _now = value;
    }
}
