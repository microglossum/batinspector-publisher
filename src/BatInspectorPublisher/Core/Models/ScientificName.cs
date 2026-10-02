namespace BatInspectorPublisher.Core.Models;

internal static class ScientificName
{
    /// <summary>
    /// Normalizes to the binomial convention: first word capitalized, all following words lower
    /// case, whitespace collapsed ("Eptesicus Serotinus" -> "Eptesicus serotinus").
    /// </summary>
    public static string Normalize(string name)
    {
        var words = name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < words.Length; i++)
        {
            var lower = words[i].ToLowerInvariant();
            words[i] = i == 0 ? char.ToUpperInvariant(lower[0]) + lower[1..] : lower;
        }

        return string.Join(' ', words);
    }
}
