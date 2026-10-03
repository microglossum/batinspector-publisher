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
    /// When the recording was made. The input time carries no offset; the reader reads it as German
    /// local time (Europe/Berlin, or the zone the entry names) and stores it with the offset of that zone on that date.
    /// <see cref="DateTimeOffset.DateTime"/> is the local wall-clock time and its date the local calendar day at the site.
    /// </summary>
    public required DateTimeOffset ObservedAt { get; init; }

    /// <summary>
    /// IANA id of the time zone <see cref="ObservedAt"/> was read in (the zone of the site). Defaults to
    /// <c>Europe/Berlin</c>, the zone of an input entry that names none.
    /// </summary>
    public string TimeZoneId { get; init; } = "Europe/Berlin";

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
