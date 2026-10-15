using System.ComponentModel.DataAnnotations;
using DevCoreBlog.Core.Newsletters;
using DevCoreBlog.Services.Newsletters;

namespace DevCoreBlog.Models.Admin;

/// <summary>Only fixed intent identities and explicit decisions cross the form boundary; no body, sender or recipient overposting.</summary>
public sealed class CampaignPrepareInput
{
    [Range(1, int.MaxValue)] public int PostId { get; set; }
    [Range(1, long.MaxValue - 1)] public long Revision { get; set; }
    public Guid StartKey { get; set; }
    public string Decision { get; set; } = "";
}
public sealed class CampaignActionInput
{
    public Guid Id { get; set; }
    [Range(1, long.MaxValue - 1)] public long Version { get; set; }
    public string Decision { get; set; } = "";
    public bool Confirm { get; set; }
}
public sealed record CampaignComposeModel(CampaignSourceSummary Source, Guid StartKey);
public sealed record CampaignDetailModel(CampaignDetail Detail, bool Ready, bool CurrentLink, DateTime UtcNow);
public sealed record CampaignListModel(CampaignListPage Page, int? PostId);
public sealed record CampaignResultModel(string Message, Guid? Id = null);
