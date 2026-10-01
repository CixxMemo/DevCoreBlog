using DevCoreBlog.Core.Entities;
namespace DevCoreBlog.Models.Public;

// Presentation only: callers supply posts already selected by the public service.
public sealed record PostCardModel(Post Post, PostCardPresentation Presentation = PostCardPresentation.Standard);

// Finite layouts share markup without dynamic utility names.
public enum PostCardPresentation { Standard, Featured, Compact }
