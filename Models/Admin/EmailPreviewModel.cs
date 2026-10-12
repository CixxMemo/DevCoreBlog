using DevCoreBlog.Services.Documents;

namespace DevCoreBlog.Models.Admin;

/// <summary>Only trusted renderer output reaches the private email view; no recipient or exit token is accepted.</summary>
public sealed record EmailPreviewModel(DocumentPreviewInput Input, RenderedEmailDocument? Rendered);
