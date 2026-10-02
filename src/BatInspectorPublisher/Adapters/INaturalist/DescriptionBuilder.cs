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

        parts.Add($"Exemplarische Ruferkennung vom {candidate.ObservedAt.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture)} Uhr (deutsche Ortszeit).");

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
}
