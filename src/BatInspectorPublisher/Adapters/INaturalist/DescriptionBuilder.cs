using System.Globalization;
using BatInspectorPublisher.Core.Models;

namespace BatInspectorPublisher.Adapters.INaturalist;

/// <summary>
/// Builds the observation description. German on purpose: it is public content on a platform and
/// all users of this package are German speaking. Kept in one place so it can be localized later.
/// </summary>
internal static class DescriptionBuilder
{
    private static readonly NumberFormatInfo German = new() { NumberDecimalSeparator = "," };

    public static string Build(ObservationCandidate candidate, string prefix)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(prefix))
        {
            parts.Add(prefix);
        }

        parts.Add($"Exemplarische Ruferkennung vom {candidate.ObservedAt.DateTime.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture)} Uhr {ZoneName(candidate)}.");

        if (candidate.TemperatureCelsius is { } temperature)
        {
            parts.Add($"Temperatur: {temperature.ToString("0.#", German)} °C.");
        }

        if (candidate.HumidityPercent is { } humidity)
        {
            parts.Add($"Luftfeuchte: {humidity.ToString("0.#", German)} %.");
        }

        if (!string.IsNullOrWhiteSpace(candidate.Comment))
        {
            parts.Add($"Anmerkung zur Bestimmung: {candidate.Comment}");
        }

        parts.Add("Beleg: Spektrogramm und Audioaufnahme dieser Ruferkennung.");
        return string.Join(" ", parts);
    }

    /// <summary>German zone abbreviations only for Europe/Berlin; any other zone is named explicitly, never guessed from the offset.</summary>
    private static string ZoneName(ObservationCandidate candidate)
    {
        var offset = candidate.ObservedAt.Offset;
        if (string.Equals(candidate.TimeZoneId, "Europe/Berlin", StringComparison.OrdinalIgnoreCase) && offset.TotalHours is 1 or 2)
        {
            return offset.TotalHours == 1 ? "MEZ" : "MESZ";
        }

        return $"(Zeitzone {candidate.TimeZoneId}, UTC{(offset < TimeSpan.Zero ? "-" : "+")}{offset:hh\\:mm})";
    }
}
