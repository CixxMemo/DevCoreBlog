using DevCoreBlog.Core.Entities;
using DevCoreBlog.Core.ReadModels;
using DevCoreBlog.Services.Rendering;

namespace DevCoreBlog.Models.Public;

/// <summary>Filtered post snapshot, trusted content and bounded related cards for the reader.</summary>
public sealed record PostDetailModel(Post Post, RenderedMarkdown Content, IReadOnlyList<PublicPostSummary> RelatedPosts);
