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

/// <summary>Body access is independent of site publication and future email delivery.</summary>
public enum PostAccessScope
{
    Public = 0,
    Subscribers = 1
}
