using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DevCoreBlog.Core.Documents;
using DevCoreBlog.Services.Documents;

/// <summary>Explicit finite documents are reviewed input; this tool never parses or converts Markdown.</summary>
internal sealed record TransitionPlan(int Version, string TargetFingerprint, string SourceDigest, string PreservedDigest, TransitionRow[] Rows);
internal sealed record TransitionRow(int Id, long EditVersion, string SourceHash, string PreservedHash, string DocumentJson);
internal sealed record PreparedRow(TransitionRow Input, PostDocumentWrite Document);
internal sealed record TransitionSnapshot(string TargetFingerprint, string Posts, string Categories, string Receipts, string Migrations, string Sequences)
{
    internal string PreservedDigest(IReadOnlySet<int> ids)
    {
        using var rows = JsonDocument.Parse(Posts);
        var preserved = string.Join('\n', rows.RootElement.EnumerateArray().Select(r => ids.Contains(r.GetProperty("Id").GetInt32()) ? TransitionHash.Preserved(r) : TransitionHash.Of(r.GetRawText())));
        return TransitionHash.Of(string.Join('\n', preserved, Categories, Receipts, Migrations, Sequences));
    }
    public string Digest => TransitionHash.Of(string.Join('\n', Posts, Categories, Receipts, Migrations, Sequences));
}
internal static class TransitionHash
{
    internal static string Of(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    internal static string Preserved(JsonElement row) => Of(JsonSerializer.Serialize(row.EnumerateObject()
        .Where(p => p.Name is not ("DocumentVersion" or "DocumentJson" or "DocumentPlainText" or "DocumentWordCount" or "DocumentReadingMinutes" or "EditVersion" or "UpdatedDate"))
        .OrderBy(p => p.Name, StringComparer.Ordinal).ToDictionary(p => p.Name, p => p.Value)));
}

/// <summary>Bounds the manifest, validates every canonical envelope and derives every persisted fact.</summary>
internal static class TransitionPlanValidation
{
    internal static PreparedRow[] Prepare(TransitionPlan plan)
    {
        if (plan.Version != 1 || plan.TargetFingerprint.Length != 64 || plan.SourceDigest.Length != 64 || plan.PreservedDigest.Length != 64 ||
            plan.Rows.Length > 100 || plan.Rows.Select(r => r.Id).Distinct().Count() != plan.Rows.Length)
            throw new InvalidOperationException();
        var validator = new ContentDocumentValidator(); var text = new DocumentTextProducer();
        return plan.Rows.Select(row => {
            if (row.Id <= 0 || row.EditVersion <= 0 || row.EditVersion == long.MaxValue || row.SourceHash.Length != 64 || row.PreservedHash.Length != 64)
                throw new InvalidOperationException();
            var validation = validator.Validate(row.DocumentJson);
            var document = validation.Document ?? throw new InvalidOperationException();
            var reading = text.Produce(document);
            return new PreparedRow(row, new(document.Document.Version, row.DocumentJson, reading.PlainText, reading.WordCount, reading.ReadingMinutes));
        }).ToArray();
    }
}
