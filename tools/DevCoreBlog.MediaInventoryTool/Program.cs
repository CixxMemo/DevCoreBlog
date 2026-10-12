using System.Text.Json;

// Operator-only dry run: the explicit provider export supplies identity, never URL guessing.
if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: MediaInventoryTool <provider-inventory.json>; connection: MEDIA_INVENTORY_CONNECTION");
    return 2;
}
try
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var cancellation = deadline.Token;
    var connection = Environment.GetEnvironmentVariable("MEDIA_INVENTORY_CONNECTION");
    if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException();
    var file = new FileInfo(args[0]);
    if (!file.Exists || file.Length > 4 * 1024 * 1024) throw new InvalidOperationException();
    await using var input = file.OpenRead();
    var assets = await JsonSerializer.DeserializeAsync<List<InventoryAsset>>(input,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true, MaxDepth = 8,
            UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow }, cancellation);
    if (assets is null || assets.Count is < 1 or > 1000 || assets.Any(asset => !asset.IsValid()) ||
        assets.Select(asset => asset.PublicId).Distinct(StringComparer.Ordinal).Count() != assets.Count)
        throw new InvalidOperationException();
    var results = assets.Select(asset => new ReferenceMatch(asset)).ToArray();
    var report = await InventoryScanner.ScanAsync(connection, results, cancellation);
    Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
catch (Exception)
{
    // Do not print provider/DB exceptions: they can include credentials or content.
    Console.Error.WriteLine("Inventory failed; no complete report. Check input, connection and schema privately.");
    return 1;
}
