namespace DevCoreBlog.Core.ReadModels;

/// <summary>Only the persisted fields required by the bounded RSS publication.</summary>
public sealed record RssPost(string Title, string Slug, string Summary, string Excerpt, DateTime PublishDate);
