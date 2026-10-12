namespace DevCoreBlog.Models.Public;

/// <summary>Each native select receives an explicit unique label target and validated selection.</summary>
public sealed record PublicKindFilterModel(string Id, string? Kind);
