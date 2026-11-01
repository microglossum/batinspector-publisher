using System.Globalization;
using BatInspectorPublisher.Core.Models;

namespace BatInspectorPublisher.Adapters.Naturgucker;

/// <summary>
/// Builds the observation's remark. German on purpose: it is public content on a platform and all users of this package are
/// German speaking. It carries what the platform has no field for: the time of day of the recording, the identification as
/// BatInspector reported it, and the measurements. There is no mention of the audio: the platform takes photos only.
/// </summary>
internal static class NaturguckerDescriptionBuilder
{
    private static readonly NumberFormatInfo German = new() { NumberDecimalSeparator = "," };

    public static string Build(ObservationCandidate candidate, NaturguckerTaxon taxon)
    {
        var parts = new List<string>
        {
            $"Exemplarische Ruferkennung vom {candidate.ObservedAt.DateTime.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture)} Uhr {ZoneName(candidate)}.",
        };

        if (taxon.Uncertain)
        {
            // A genus or a group value: say what BatInspector actually reported.
            parts.Add($"Bestimmung in BatInspector: \"{candidate.ScientificName}\" (keine sichere Artbestimmung), hier als \"{taxon}\" eingetragen.");
        }

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

        parts.Add("Beleg: Spektrogramm dieser Ruferkennung.");
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
