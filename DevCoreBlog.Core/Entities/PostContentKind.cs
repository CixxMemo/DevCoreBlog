namespace DevCoreBlog.Core.Entities;

/// <summary>Stable stored values; legacy content is never classified by inference.</summary>
public enum PostContentKind
{
    Unclassified = 0,
    AiNews = 1,
    Experience = 2,
    Philosophy = 3,
    Newsletter = 4
}

/// <summary>Stored history of how a post was classified. Since 23 October 2026 it no longer limits who can read a published post.</summary>
public enum PostAccessScope
{
    Public = 0,
    Subscribers = 1
}
