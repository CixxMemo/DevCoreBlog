using System.Globalization;
using DevCoreBlog.Services.Publishing;

namespace DevCoreBlog.Models.Presentation;

/// <summary>Formats stored UTC instants for the English UI in the configured site timezone.</summary>
public sealed class SiteDateFormatter(PublicationTimeZone timeZone)
{
    // Browsers require IANA names even when the host accepts a Windows timezone ID.
    public string BrowserTimeZoneId => TimeZoneInfo.TryConvertWindowsIdToIanaId(timeZone.Id, out var ianaId)
        ? ianaId : timeZone.Id;

    public string Date(DateTime storedUtc) => timeZone.ToSiteTime(storedUtc)
        .ToString("MMM dd, yyyy", CultureInfo.GetCultureInfo("en-US"));
}
