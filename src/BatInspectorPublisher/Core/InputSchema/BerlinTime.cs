namespace BatInspectorPublisher.Core.InputSchema;

/// <summary>
/// The time zone of the input file: German local time (Europe/Berlin), never the zone of the host.
/// There is deliberately no fallback when the zone data is missing: a guessed offset would put a wrong
/// time on a public record.
/// </summary>
internal static class BerlinTime
{
    /// <summary>IANA id of the zone that applies when an entry names none.</summary>
    public const string DefaultZoneId = "Europe/Berlin";

    private static readonly string[] ZoneIds = [DefaultZoneId, "W. Europe Standard Time"];

    /// <summary>Resolves an IANA zone id named in the input file; null if the host does not know it.</summary>
    public static TimeZoneInfo? TryFindZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Resolves the default zone (Europe/Berlin) from the host's time zone data.</summary>
    /// <exception cref="TimeZoneNotFoundException">The host has no Europe/Berlin zone data (e.g. a container without tzdata).</exception>
    public static TimeZoneInfo FindZone()
    {
        foreach (var id in ZoneIds)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
            }
        }

        throw new TimeZoneNotFoundException(
            "The time zone Europe/Berlin is not available on this machine (install the tz database, e.g. the tzdata package). "
            + "Input times are German local time and are not interpreted without it.");
    }

    /// <summary>
    /// Attaches the zone's offset to a wall-clock time. Returns an error message instead of a result for a time that
    /// does not exist (spring-forward gap) or whose UTC instant lies outside the supported range (years near 1 or 9999).
    /// A time in the repeated autumn hour is ambiguous and is read as standard time, which is what
    /// <see cref="TimeZoneInfo"/> picks.
    /// </summary>
    public static string? TryConvert(TimeZoneInfo zone, DateTime wallClock, out DateTimeOffset result)
    {
        result = default;
        if (zone.IsInvalidTime(wallClock))
        {
            return "does not exist in local time (the clocks go forward at that moment).";
        }

        try
        {
            result = new DateTimeOffset(wallClock, zone.GetUtcOffset(wallClock));
            return null;
        }
        catch (ArgumentOutOfRangeException)
        {
            return "is outside the supported range of dates.";
        }
    }
}
