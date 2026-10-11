namespace DevCoreBlog.Models.Admin;

/// <summary>Commit correlation only; local revisions never grant authorization or replace DB concurrency.</summary>
public sealed record DocumentRecoveryReceipt(int PostId, int SourcePostId, string Revision);
