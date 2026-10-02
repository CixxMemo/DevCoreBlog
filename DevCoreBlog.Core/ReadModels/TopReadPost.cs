namespace DevCoreBlog.Core.ReadModels;

/// <summary>Only the post fields displayed in read rankings; callers enforce visibility.</summary>
public sealed record TopReadPost(
    int Id,
    string Title,
    string Slug,
    string CategoryName,
    int ViewCount);
