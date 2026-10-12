using System.Text.Json;

/// <summary>Builds a manifest from explicit JSON replacements tied to exact source row hashes.</summary>
internal static class ManifestAuthoring
{
    internal static TransitionPlan Build(TransitionSnapshot snapshot, TransitionDocuments documents)
    {
        if (documents.Version != 1 || documents.Rows.Length > 100 || documents.Rows.Select(r => r.Id).Distinct().Count() != documents.Rows.Length)
            throw new InvalidOperationException();
        using var json = JsonDocument.Parse(snapshot.Posts);
        var legacy = json.RootElement.EnumerateArray().Where(r => r.GetProperty("IsActive").GetBoolean() && TransitionDatabase.Legacy(r)).ToDictionary(r => r.GetProperty("Id").GetInt32());
        if (!legacy.Keys.ToHashSet().SetEquals(documents.Rows.Select(r => r.Id))) throw new InvalidOperationException();
        var rows = documents.Rows.Select(input => {
            var row = legacy[input.Id];
            if (TransitionHash.Of(row.GetRawText()) != input.SourceHash) throw new InvalidOperationException();
            return new TransitionRow(input.Id, row.GetProperty("EditVersion").GetInt64(), input.SourceHash, TransitionHash.Preserved(row), input.DocumentJson);
        }).ToArray();
        var plan = new TransitionPlan(1, snapshot.TargetFingerprint, snapshot.Digest, snapshot.PreservedDigest(legacy.Keys.ToHashSet()), rows);
        TransitionPlanValidation.Prepare(plan);
        return plan;
    }
}
internal sealed record TransitionDocuments(int Version, TransitionDocumentEntry[] Rows);
internal sealed record TransitionDocumentEntry(int Id, string SourceHash, string DocumentJson);
