using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

/// <summary>Reads a bounded, consistent PostgreSQL snapshot with enforced read-only settings.</summary>
internal static class InventoryReader
{
    public static async Task ReadAsync(string connectionString, InventoryReport report, CancellationToken token)
    {
        var settings = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Timeout = 5, CommandTimeout = 10, CancellationTimeout = 1000,
            Pooling = false, Enlist = false, IncludeErrorDetail = false,
            ApplicationName = "DevCoreBlog.ReadOnlyInventory"
        };
        report.TargetFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join('|', settings.Host, settings.Port, settings.Database, settings.Username))));
        report.ConnectionScope = settings.Host is "localhost" or "127.0.0.1" or "::1"
            ? "configured_loopback" : "configured_external_or_socket";
        settings.Options = string.Join(' ', settings.Options,
            "-c default_transaction_read_only=on -c statement_timeout=10000 -c lock_timeout=1000 -c idle_in_transaction_session_timeout=15000 -c timezone=UTC");
        report.Stage = "connection";
        await using var dataSource = NpgsqlDataSource.Create(settings.ConnectionString);
        await using var connection = await dataSource.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
            await readOnly.ExecuteNonQueryAsync(token);

        async Task<JsonElement> Read(string name, string sql)
        {
            report.Stage = name;
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            var value = await command.ExecuteScalarAsync(token) as string;
            if (value is null || value.Length > 65536) throw new InvalidOperationException();
            using var document = JsonDocument.Parse(value);
            var section = document.RootElement.Clone();
            report.Sections.Add(name, section);
            return section;
        }

        var metadata = await Read("environment", InventoryQueries.Environment);
        if (metadata.GetProperty("transactionReadOnly").GetString() != "on" ||
            metadata.GetProperty("defaultTransactionReadOnly").GetString() != "on")
            throw new InvalidOperationException();
        var schema = await Read("schema", InventoryQueries.Schema);
        var columns = schema.GetProperty("postColumns").EnumerateArray()
            .Select(value => value.GetString()).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var required = new[] { "Id", "Title", "Content", "Summary", "Excerpt", "Slug", "CategoryId",
            "IsActive", "IsPublished", "PublishDate", "ThumbnailUrl" };
        if (!required.All(columns.Contains) || !schema.GetProperty("categoriesPresent").GetBoolean())
            throw new InvalidOperationException();
        // A half-applied access schema cannot be interpreted as legacy public content.
        if (columns.Contains("ContentKind") != columns.Contains("AccessScope"))
            throw new InvalidOperationException();

        var totals = await Read("totals", InventoryQueries.Totals);
        if (totals.GetProperty("posts").GetInt64() > 50000 ||
            totals.GetProperty("contentBytes").GetInt64() > 67108864)
            throw new InvalidOperationException();
        await Read("posts", InventoryQueries.Posts(columns));
        await Read("categories", InventoryQueries.Categories);
        if (schema.GetProperty("receiptsPresent").GetBoolean())
            await Read("webhookReceipts", InventoryQueries.Receipts);
        if (schema.GetProperty("migrationsPresent").GetBoolean())
            await Read("migrations", InventoryQueries.Migrations);
        report.Stage = "read_only_snapshot_complete";
        await transaction.RollbackAsync(token);
    }
}
