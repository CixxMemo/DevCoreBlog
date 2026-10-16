namespace DevCoreBlog.Services.Publishing;

/// <summary>
/// Converts publication instants between UTC storage and the configured site clock.
/// </summary>
public sealed class PublicationTimeZone
{
    private readonly TimeZoneInfo _timeZone;

    public PublicationTimeZone(TimeZoneInfo timeZone)
    {
        _timeZone = timeZone;
    }

    public string Id => _timeZone.Id;

    /// <summary>
    /// Treats timezone-free form values as site time and keeps explicit instants in UTC.
    /// </summary>
    public bool TryConvertToUtc(DateTime value, out DateTime utc, out string? error)
    {
        utc = default;
        error = null;

        if (value == default)
        {
            error = "Yayın zamanı gereklidir.";
            return false;
        }

        if (value.Kind == DateTimeKind.Unspecified)
        {
            if (_timeZone.IsInvalidTime(value))
            {
                error = "Bu zaman site saat diliminde bulunmuyor.";
                return false;
            }

            if (_timeZone.IsAmbiguousTime(value))
            {
                error = "Bu zaman site saat diliminde iki kez oluşuyor. Başka bir zaman seçin.";
                return false;
            }
        }

        try
        {
            utc = value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => TimeZoneInfo.ConvertTimeToUtc(value, _timeZone)
            };
            return true;
        }
        catch (ArgumentException)
        {
            error = "Yayın zamanı desteklenen aralığın dışında.";
            return false;
        }
    }

    /// <summary>
    /// Reads database timestamps as UTC even if an older provider omitted DateTimeKind.
    /// </summary>
    public DateTime ToSiteTime(DateTime storedUtc)
    {
        var utc = storedUtc.Kind == DateTimeKind.Local
            ? storedUtc.ToUniversalTime()
            : DateTime.SpecifyKind(storedUtc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, _timeZone);
    }
}
