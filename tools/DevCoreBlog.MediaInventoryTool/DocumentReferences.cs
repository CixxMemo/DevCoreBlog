using DevCoreBlog.Core.Documents;
using DevCoreBlog.Services.Documents;

/// <summary>Rejects partial/unknown storage before extracting typed image sources; never scans raw JSON keys.</summary>
internal static class DocumentReferences
{
    public static (string Status, string[] Sources) Read(int? version, string? json, string? plainText,
        int? words, int? minutes, ContentDocumentValidator validator, DocumentTextProducer producer)
    {
        if (version is null && json is null && plainText is null && words is null && minutes is null)
            return ("Legacy", []);
        if (version != DocumentLimits.Version || json is null) return ("Unknown or partial document", []);
        var validation = validator.Validate(json);
        if (validation.Document is not { } document) return ("Invalid document", []);
        var reading = producer.Produce(document);
        if (plainText != reading.PlainText || words != reading.WordCount || minutes != reading.ReadingMinutes)
            return ("Inconsistent document facts", []);
        var sources = new List<string>();
        Collect(document.Document.Root, sources);
        return ("Validated", sources.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static void Collect(DocumentNode node, List<string> sources)
    {
        if (node.Attributes is ImageAttributes image) sources.Add(ReferenceMatch.Normalize(image.Source));
        foreach (var child in node.Children) Collect(child, sources);
    }
}
