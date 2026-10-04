using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using DevCoreBlog.Core.ReadModels;
using DevCoreBlog.Data;
using DevCoreBlog.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

// Measure actual repository reads and their PostgreSQL plans, never production data.
var connection = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING")
    ?? throw new InvalidOperationException("An isolated connection is required.");
var parsed = new NpgsqlConnectionStringBuilder(connection);
if (parsed.Host != "127.0.0.1" || parsed.Port != 55449 || parsed.Database != "devcoreblog_f01_test")
    throw new InvalidOperationException("Only the F48 disposable fixture is allowed.");
var now = new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc);
var results = new Dictionary<string, object>();
foreach (var path in new[] { "home", "detail", "search", "admin", "related" })
{
    var times = new List<double>();
    object? result = null;
    var capture = new QueryCapture();
    var rows = 0;
    for (var iteration = 0; iteration < 23; iteration++)
    {
        capture.Commands.Clear();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connection).AddInterceptors(capture).Options;
        await using var context = new ApplicationDbContext(options);
        var repository = new PostRepository(context);
        var watch = Stopwatch.StartNew();
        switch (path)
        {
            case "home":
                var home = await repository.GetPublishedPostsPagedAsync(1, 9, now);
                result = home.Posts; rows = home.Posts.Count;
                break;
            case "detail":
                result = await repository.GetPostBySlugAsync("f48-post-1", now);
                rows = result is null ? 0 : 1;
                break;
            case "search":
                var search = await repository.SearchPostsPagedAsync("needle", null, 1, 9, now);
                result = search.Posts; rows = search.Posts.Count;
                break;
            case "admin":
                var admin = await repository.GetAdminPostsPagedAsync(new AdminPostQuery(), now);
                result = admin.Posts; rows = admin.Posts.Count;
                break;
            case "related":
                var related = (await repository.GetRelatedPostsAsync(10001, 1001, now)).ToList();
                result = related; rows = related.Count;
                break;
        }
        watch.Stop();
        if (context.ChangeTracker.Entries().Any()) throw new InvalidOperationException("Read tracked entities.");
        if (iteration >= 3) times.Add(watch.Elapsed.TotalMilliseconds);
    }
    times.Sort();
    await using var database = new NpgsqlConnection(connection);
    await database.OpenAsync();
    var plans = new List<object>();
    foreach (var captured in capture.Commands)
    {
        await using var explain = new NpgsqlCommand("EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + captured.Sql, database);
        foreach (var parameter in captured.Parameters) explain.Parameters.Add(parameter.Clone());
        var plan = (string?)await explain.ExecuteScalarAsync() ?? throw new InvalidOperationException("Missing plan.");
        plans.Add(new { sql = captured.Sql, plan = JsonSerializer.Deserialize<JsonElement>(plan) });
    }
    results[path] = new { medianMs = times[times.Count / 2], p95Ms = times[18], samples = times,
        queryCount = capture.Commands.Count, materializedRows = rows, trackedEntities = 0,
        serializedResultBytes = JsonSerializer.SerializeToUtf8Bytes(result,
            new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles }).Length,
        plans };
}
Console.WriteLine(JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));

// Parameters stay in memory and are used only to explain the captured read commands.
sealed class QueryCapture : DbCommandInterceptor
{
    public List<(string Sql, NpgsqlParameter[] Parameters)> Commands { get; } = [];
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Commands.Add((command.CommandText, command.Parameters.Cast<NpgsqlParameter>().Select(p => p.Clone()).ToArray()));
        return ValueTask.FromResult(result);
    }
}
