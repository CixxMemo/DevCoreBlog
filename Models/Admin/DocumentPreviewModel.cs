using DevCoreBlog.Services.Documents;

namespace DevCoreBlog.Models.Admin;

/// <summary>Keeps authored input encoded in the form and trusted output separate.</summary>
public sealed record DocumentPreviewModel(DocumentPreviewInput Input, RenderedContentDocument? Rendered);
