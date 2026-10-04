using System.Data;
using System.Text.Json;
using DevCoreBlog.Data;
using Microsoft.EntityFrameworkCore;

// Operator-only dry run: the explicit provider export supplies identity, never URL guessing.
if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: MediaInventoryTool <provider-inventory.json>; connection: MEDIA_INVENTORY_CONNECTION");
    return 2;
}
try
{
    var connection = Environment.GetEnvironmentVariable("MEDIA_INVENTORY_CONNECTION");
    if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException();
    var file = new FileInfo(args[0]);
    if (!file.Exists || file.Length > 4 * 1024 * 1024) throw new InvalidOperationException();
    await using var input = file.OpenRead();
    var assets = await JsonSerializer.DeserializeAsync<List<InventoryAsset>>(input,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    if (assets is null || assets.Count is < 1 or > 1000 || assets.Any(asset => !asset.IsValid()) ||
        assets.Select(asset => asset.PublicId).Distinct(StringComparer.Ordinal).Count() != assets.Count)
        throw new InvalidOperationException();
    var results = assets.Select(asset => new ReferenceMatch(asset)).ToArray();
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(connection, provider => provider.CommandTimeout(120)).Options;
    await using var db = new ApplicationDbContext(options);
    await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
    await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY");
    var startedAt = DateTimeOffset.UtcNow;
    var scanned = 0;
    // Stream a narrow projection of every post, including drafts/inactive posts, without tracking.
    await foreach (var post in db.Posts.AsNoTracking().OrderBy(post => post.Id)
        .Select(post => new { post.Id, post.ThumbnailPublicId, post.ThumbnailUrl,
            post.Content, post.Summary, post.Excerpt }).AsAsyncEnumerable())
    {
        scanned++;
        var texts = new[] { post.ThumbnailUrl, post.Content, post.Summary, post.Excerpt }
            .Select(ReferenceMatch.Normalize).ToArray();
        foreach (var match in results) match.Observe(post.Id, post.ThumbnailPublicId, texts);
    }
    await snapshot.CommitAsync();
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        dryRun = true, snapshotStartedAt = startedAt, scannedPosts = scanned,
        warning = "No deletion permission. Explicit URL aliases are required for transformed/legacy URLs. Unsaved drafts, external sites and uploads after this snapshot are not covered.",
        assets = results.Select(match => match.Report())
    }, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
catch (Exception)
{
    // Do not print provider/DB exceptions: they can include credentials or content.
    Console.Error.WriteLine("Inventory failed; no complete report. Check input, connection and schema privately.");
    return 1;
}
