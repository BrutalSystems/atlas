namespace Atlas.Email;

/// <summary>
/// Shared "since" resolution for GetRecentFolderStatsAsync across providers. Calendar-day
/// aligned to the given IANA time zone (falling back to UTC when unset/unrecognized): days=0
/// is since the start of today in that zone, days=1 since the start of yesterday, and so on --
/// not a rolling N*24-hour window from the current instant.
/// </summary>
internal static class RecentWindow
{
    public static DateTimeOffset Since(int days, string? timeZoneId = null)
    {
        var tz = ResolveTimeZone(timeZoneId);
        var nowInZone = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz);
        var localMidnight = DateTime.SpecifyKind(nowInZone.Date.AddDays(-days), DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localMidnight, tz), TimeSpan.Zero);
    }

    private static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId)) return TimeZoneInfo.Utc;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
