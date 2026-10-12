using DevCoreBlog.Core.Documents;

namespace DevCoreBlog.Services.Documents;

/// <summary>Revalidates a single stored snapshot; admin and public rendering share this trust boundary.</summary>
internal static class StoredDocumentReader
{
    internal static PostDocumentReadResult Read(StoredPostDocument stored,
        ContentDocumentValidator validator, DocumentTextProducer textProducer)
    {
        if (stored is { Version: null, Json: null, PlainText: null, WordCount: null, ReadingMinutes: null })
            return new(PostDocumentReadStatus.Legacy, stored.EditVersion);
        if (stored.Version != DocumentLimits.Version || stored.Json is null)
            return new(PostDocumentReadStatus.Inconsistent, stored.EditVersion);
        var validation = validator.Validate(stored.Json);
        if (validation.Document is not { } document)
            return new(PostDocumentReadStatus.Inconsistent, stored.EditVersion);
        var reading = textProducer.Produce(document);
        if (reading.PlainText != stored.PlainText || reading.WordCount != stored.WordCount ||
            reading.ReadingMinutes != stored.ReadingMinutes)
            return new(PostDocumentReadStatus.Inconsistent, stored.EditVersion);
        return new(PostDocumentReadStatus.Ready, stored.EditVersion, document, reading, stored.Json);
    }
}
