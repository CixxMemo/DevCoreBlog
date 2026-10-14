using System.Globalization;
using DevCoreBlog.Services.Publishing;

namespace DevCoreBlog.Models.Presentation;

/// <summary>Formats stored UTC instants in Turkish using the configured site timezone.</summary>
public sealed class SiteDateFormatter(PublicationTimeZone timeZone)
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    // Browsers require IANA names even when the host accepts a Windows timezone ID.
    public string BrowserTimeZoneId => TimeZoneInfo.TryConvertWindowsIdToIanaId(timeZone.Id, out var ianaId)
        ? ianaId : timeZone.Id;

    public string Year(DateTime storedUtc) => timeZone.ToSiteTime(storedUtc)
        .ToString("yyyy", Turkish);

    public string Date(DateTime storedUtc) => timeZone.ToSiteTime(storedUtc)
        .ToString("dd MMM yyyy", Turkish);

    /// <summary>Operational and recovery times expose the same explicit timezone label.</summary>
    public string DateAndTime(DateTime storedUtc) =>
        $"{timeZone.ToSiteTime(storedUtc).ToString("dd MMM yyyy HH:mm", Turkish)} ({BrowserTimeZoneId})";

    public string DateAndTime(DateTimeOffset instant) => DateAndTime(instant.UtcDateTime);
}
