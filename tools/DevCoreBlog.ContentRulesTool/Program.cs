using DevCoreBlog.Core.Entities;
using DevCoreBlog.Core.Validation;
using DevCoreBlog.Data;
using DevCoreBlog.Data.Repositories;
using DevCoreBlog.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Caching.Memory;

var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("DB_CONNECTION_STRING is required.");
    return 2;
}

var options = new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseNpgsql(connectionString)
    .Options;
await using var context = new ApplicationDbContext(options);
var postRepository = new PostRepository(context);
var categoryRepository = new CategoryRepository(
    context,
    NullLogger<CategoryRepository>.Instance);
using var cache = new MemoryCache(new MemoryCacheOptions());
var postService = new PostService(postRepository, cache, categoryRepository);
var categoryService = new CategoryService(categoryRepository);

var conflicts = new Dictionary<string, int>
{
    ["post_title"] = await context.Posts.CountAsync(
        post => string.IsNullOrWhiteSpace(post.Title) ||
            post.Title.Length > PostContentRules.MaximumTitleLength),
    ["post_summary"] = await context.Posts.CountAsync(
        post => post.Summary.Length > PostContentRules.MaximumSummaryLength),
    ["post_excerpt"] = await context.Posts.CountAsync(
        post => post.Excerpt.Length > PostContentRules.MaximumExcerptLength),
    ["post_content"] = await context.Posts.CountAsync(
        post => string.IsNullOrWhiteSpace(post.Content) ||
            post.Content.Length > PostContentRules.MaximumContentLength),
    ["post_thumbnail_url"] = await context.Posts.CountAsync(
        post => post.ThumbnailUrl.Length > PostContentRules.MaximumThumbnailUrlLength ||
            (post.ThumbnailUrl != string.Empty && !post.ThumbnailUrl.StartsWith("https://"))),
    ["post_category"] = await context.Posts.CountAsync(
        post => !post.Category.IsActive),
    ["category_name"] = await context.Categories.CountAsync(
        category => string.IsNullOrWhiteSpace(category.Name) ||
            category.Name.Length > CategoryContentRules.MaximumNameLength)
};

var originalPostCount = await context.Posts.CountAsync();
var originalCategoryCount = await context.Categories.CountAsync();

var whitespacePost = ValidPost("   ");
var whitespaceResult = await postService.CreatePostAsync(whitespacePost);
var inactiveCategoryPost = ValidPost("F12 inactive category");
inactiveCategoryPost.CategoryId = 1002;
var inactiveCategoryResult = await postService.CreatePostAsync(inactiveCategoryPost);
var unsafeUrlPost = ValidPost("F12 unsafe URL");
unsafeUrlPost.ThumbnailUrl = "http://images.example.test/unsafe.png";
var unsafeUrlResult = await postService.CreatePostAsync(unsafeUrlPost);
var oversizedPost = ValidPost("F12 oversized content");
oversizedPost.Content = new string('x', PostContentRules.MaximumContentLength + 1);
var oversizedResult = await postService.CreatePostAsync(oversizedPost);

var invalidCategory = new Category { Name = "   " };
var invalidCategoryResult = await categoryService.CreateCategoryAsync(invalidCategory);

var normalizedPost = ValidPost("  F12 normalized post  ");
normalizedPost.Summary = "   ";
normalizedPost.Excerpt = "   ";
var normalizedResult = await postService.CreatePostAsync(normalizedPost);
var storedNormalizedPost = await context.Posts
    .AsNoTracking()
    .SingleOrDefaultAsync(post => post.Id == normalizedPost.Id);

var finalPostCount = await context.Posts.CountAsync();
var finalCategoryCount = await context.Categories.CountAsync();
var checks = new Dictionary<string, bool>
{
    ["fixture_conflicts_are_reported_before_writes"] =
        conflicts["post_summary"] == 1 &&
        conflicts["post_category"] == 1 &&
        conflicts.Where(pair => pair.Key is not "post_summary" and not "post_category")
            .All(pair => pair.Value == 0),
    ["direct_service_rejects_whitespace_title"] =
        !whitespaceResult.IsValid && HasField(whitespaceResult, nameof(Post.Title)),
    ["direct_service_rejects_inactive_category"] =
        !inactiveCategoryResult.IsValid &&
        HasField(inactiveCategoryResult, nameof(Post.CategoryId)),
    ["direct_service_rejects_non_https_cover"] =
        !unsafeUrlResult.IsValid &&
        HasField(unsafeUrlResult, nameof(Post.ThumbnailUrl)),
    ["direct_service_rejects_oversized_content"] =
        !oversizedResult.IsValid && HasField(oversizedResult, nameof(Post.Content)),
    ["direct_category_service_rejects_blank_name"] =
        !invalidCategoryResult.IsValid &&
        HasField(invalidCategoryResult, nameof(Category.Name)),
    ["optional_text_is_normalized_and_persisted"] =
        normalizedResult.IsValid &&
        normalizedPost.Title == "F12 normalized post" &&
        storedNormalizedPost?.Summary == string.Empty &&
        storedNormalizedPost.Excerpt == string.Empty,
    ["rejected_service_calls_do_not_write"] =
        finalPostCount == originalPostCount + 1 &&
        finalCategoryCount == originalCategoryCount
};

Console.WriteLine("F12 isolated data compatibility inventory");
foreach (var conflict in conflicts)
{
    Console.WriteLine($"existing_{conflict.Key}_conflicts={conflict.Value}");
}

Console.WriteLine("F12 direct service validation verification");
foreach (var check in checks)
{
    Console.WriteLine($"{check.Key}={check.Value.ToString().ToLowerInvariant()}");
}

return checks.Values.All(static passed => passed) ? 0 : 1;

static Post ValidPost(string title) => new()
{
    Title = title,
    Content = "F12 direct service content",
    Summary = "F12 summary",
    Excerpt = "F12 excerpt",
    ThumbnailUrl = "https://images.example.test/f12.png",
    CategoryId = 1001,
    IsPublished = false,
    IsActive = true,
    PublishDate = DateTime.UtcNow
};

static bool HasField(ContentValidationResult result, string field) =>
    result.Errors.Any(error => error.Field == field);
