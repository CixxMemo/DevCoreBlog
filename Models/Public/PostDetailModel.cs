using DevCoreBlog.Core.Entities;
using DevCoreBlog.Core.ReadModels;
using DevCoreBlog.Services.Documents;

namespace DevCoreBlog.Models.Public;

/// <summary>Filtered post snapshot, trusted content and bounded related cards for the reader.</summary>
public sealed record PostDetailModel(Post Post, PublicPostContent Content, IReadOnlyList<PublicPostSummary> RelatedPosts);
