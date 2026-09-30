using DevCoreBlog.Core.Entities;
using DevCoreBlog.Core.Publishing;

namespace DevCoreBlog.Models.Admin;

/// <summary>Provides the view with a computed state without making publication decisions in Razor.</summary>
public sealed record PostListItem(Post Post, PostPublicationState State);
