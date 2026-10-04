using DevCoreBlog.Services.Operations;
namespace DevCoreBlog.Models.Admin;

/// <summary>Contains safe admin-only service observations and presentation context.</summary>
public sealed record AdminDashboardViewModel(AdminOverviewSnapshot Overview, string EnvironmentName, bool WebhookConfigured);
