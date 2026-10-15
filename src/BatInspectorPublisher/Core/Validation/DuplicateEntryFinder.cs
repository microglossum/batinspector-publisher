using BatInspectorPublisher.Core.InputSchema;
using BatInspectorPublisher.Core.Models;

namespace BatInspectorPublisher.Core.Validation;

/// <summary>
/// Finds entries of one file that repeat an earlier one: same species, same instant and same position. The file is meant
/// to hold one reference recording per species, night and place, and an exact repeat is almost certainly an export
/// mistake. The platform's own duplicate check catches it too, but can lag behind a just-created observation.
/// </summary>
internal sealed class DuplicateEntryFinder
{
    private readonly Dictionary<(string Name, DateTimeOffset At, double Latitude, double Longitude), int> _seen = [];

    /// <summary>
    /// Remembers the entry and returns a warning (at <paramref name="path"/>) when an earlier entry has the same species,
    /// time and position.
    /// </summary>
    public ValidationIssue? Check(int index, string path, ObservationCandidate candidate)
    {
        var key = (candidate.ScientificName, candidate.ObservedAt, candidate.Latitude, candidate.Longitude);
        if (_seen.TryGetValue(key, out var first))
        {
            return new(path, $"Same species, time and position as DocumentFiles[{first}]; both would be published unless the platform's duplicate check catches it.");
        }

        _seen[key] = index;
        return null;
    }
}
