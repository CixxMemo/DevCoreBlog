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
            error = "Publish date is required.";
            return false;
        }

        if (value.Kind == DateTimeKind.Unspecified)
        {
            if (_timeZone.IsInvalidTime(value))
            {
                error = "Publish date does not exist in the site time zone.";
                return false;
            }

            if (_timeZone.IsAmbiguousTime(value))
            {
                error = "Publish date occurs twice in the site time zone. Choose another time.";
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
            error = "Publish date is outside the supported range.";
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
