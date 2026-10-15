using DevCoreBlog.Core.Subscriptions;
using DevCoreBlog.Services.Subscriptions;
namespace DevCoreBlog.Models.Admin;

/// <summary>Private server-rendered state; navigation contains only date/status/page, never an address.</summary>
public sealed record SubscriberListModel(SubscriberListPage Data, int Page, SubscriberListStatus Status, SubscriberDateRange Dates);
