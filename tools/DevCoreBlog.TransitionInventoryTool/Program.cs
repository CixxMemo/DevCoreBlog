using System.Text.Json;

// Operator-only read: never start the MVC host, migrations, media calls or a writer.
var report = new InventoryReport();
try
{
    if (args.Length != 0) throw new ArgumentException();
    var connection = Environment.GetEnvironmentVariable("TRANSITION_INVENTORY_CONNECTION");
    if (string.IsNullOrWhiteSpace(connection)) throw new ArgumentException();
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    await InventoryReader.ReadAsync(connection, report, deadline.Token);
    report.Status = "verified";
}
catch (OperationCanceledException)
{
    report.Status = "unverified";
    report.Failure = "deadline_or_cancelled";
}
catch (Exception)
{
    // Provider/config exceptions can contain credentials or content. Export only fixed labels.
    report.Status = "unverified";
    report.Failure = "configuration_connection_schema_or_query_failed";
}
Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true
}));
return report.Status == "verified" ? 0 : 1;

/// <summary>Only safe aggregate sections are retained, including when later reads fail.</summary>
internal sealed class InventoryReport
{
    public string Status { get; set; } = "unverified";
    public string Stage { get; set; } = "configuration";
    public string? Failure { get; set; }
    public DateTimeOffset StartedAtUtc { get; } = DateTimeOffset.UtcNow;
    public string? TargetFingerprint { get; set; }
    public string? ConnectionScope { get; set; }
    public Dictionary<string, JsonElement> Sections { get; } = [];
    public bool NoWriteOperations => true;
    public string UnverifiedCountMeaning => "Missing or failed counts are unknown, never zero.";
}
