using System.Text.Json;
using System.Text.Json.Serialization;

namespace BatInspectorPublisher.Adapters.INaturalist;

internal sealed class OAuthTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }
}

internal sealed class TaxaAutocompleteResponse
{
    [JsonPropertyName("results")]
    public List<Taxon> Results { get; set; } = [];
}

internal sealed class Taxon
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("rank")]
    public string? Rank { get; set; }
}

internal sealed class ObservationsSearchResponse
{
    [JsonPropertyName("total_results")]
    public int TotalResults { get; set; }

    [JsonPropertyName("results")]
    public List<ExistingObservation> Results { get; set; } = [];
}

/// <summary>An observation returned by the v1 search. Photos and sounds stay null when the response does not say, so "unknown" is never read as "none".</summary>
internal sealed class ExistingObservation
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("photos")]
    public List<JsonElement>? Photos { get; set; }

    [JsonPropertyName("sounds")]
    public List<JsonElement>? Sounds { get; set; }
}

internal sealed class CreateObservationRequest
{
    [JsonPropertyName("observation")]
    public ObservationPayload Observation { get; set; } = new();
}

internal sealed class ObservationPayload
{
    // No "time_observed_at" on purpose: the real v2 API rejects it with 422
    // "must NOT have additional properties".
    [JsonPropertyName("taxon_id")]
    public int TaxonId { get; set; }

    [JsonPropertyName("observed_on_string")]
    public string ObservedOnString { get; set; } = string.Empty;

    [JsonPropertyName("latitude")]
    public double Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public double Longitude { get; set; }

    // The v2 API validates place_guess as "must be string" and rejects an explicit null.
    [JsonPropertyName("place_guess")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PlaceGuess { get; set; }

    [JsonPropertyName("species_guess")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SpeciesGuess { get; set; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("tag_list")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TagList { get; set; }
}

internal sealed class CreateObservationResult
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = string.Empty;
}

/// <summary>
/// The real v2 POST /observations response wraps the created observation in a results array like
/// every v2 list endpoint; it is NOT a bare observation object. Deserializing it as a bare object
/// silently yields Id=0/Uuid="" instead of failing.
/// </summary>
internal sealed class CreateObservationResponse
{
    [JsonPropertyName("results")]
    public List<CreateObservationResult> Results { get; set; } = [];
}
