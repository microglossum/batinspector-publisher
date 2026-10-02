namespace BatInspectorPublisher.Core.Models;

/// <summary>
/// One exemplar bat recording that should be published as a single observation.
/// Platform-neutral: adapters translate it into whatever their platform expects.
/// </summary>
public sealed record ObservationCandidate
{
    /// <summary>Latin species (or genus) name, normalized to binomial capitalization ("Genus species").</summary>
    public required string ScientificName { get; init; }

    /// <summary>Local (German) species name, if BatInspector provided one.</summary>
    public string? LocalName { get; init; }

    /// <summary>
    /// When the recording was made, as German local wall-clock time (Europe/Berlin). The input
    /// format carries no time zone, so <see cref="DateTime.Kind"/> is <see cref="DateTimeKind.Unspecified"/>.
    /// </summary>
    public required DateTime ObservedAt { get; init; }

    /// <summary>WGS84 latitude in degrees.</summary>
    public required double Latitude { get; init; }

    /// <summary>WGS84 longitude in degrees.</summary>
    public required double Longitude { get; init; }

    /// <summary>Air temperature at the recorder in °C, if measured.</summary>
    public double? TemperatureCelsius { get; init; }

    /// <summary>Relative humidity at the recorder in %, if measured.</summary>
    public double? HumidityPercent { get; init; }

    /// <summary>Free-text remark from the person who verified the identification (e.g. certainty).</summary>
    public string? Comment { get; init; }

    /// <summary>Path to the spectrogram image of the exemplar call (photo evidence).</summary>
    public required string SpectrogramPath { get; init; }

    /// <summary>Path to the audio recording of the exemplar call (sound evidence).</summary>
    public required string AudioPath { get; init; }
}
