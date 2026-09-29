namespace DevCoreBlog.Core.ReadModels;

/// <summary>Only the post fields displayed in the admin read ranking.</summary>
public sealed record TopReadPost(
    int Id,
    string Title,
    string Slug,
    string CategoryName,
    int ViewCount);
