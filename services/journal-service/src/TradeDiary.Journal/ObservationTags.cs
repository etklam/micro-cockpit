/// <summary>
/// Observation tag normalization.
/// Normalized form: trim, ToLowerInvariant, reject empty/control, max 32 chars, max 10 unique tags.
/// </summary>
static class ObservationTags
{
    internal const int MaxTagLength = 32;
    internal const int MaxTagsPerObservation = 10;

    internal static string? NormalizeAll(IReadOnlyList<string>? input, out IReadOnlyList<string> tags)
    {
        tags = Array.Empty<string>();
        if (input is null || input.Count == 0) return null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<string>();
        foreach (var raw in input)
        {
            if (!TryNormalizeOne(raw, out var tag, out var error)) return error;
            if (seen.Add(tag!)) ordered.Add(tag!);
        }
        if (ordered.Count > MaxTagsPerObservation) return "too_many_tags";
        tags = ordered;
        return null;
    }

    internal static bool TryNormalizeOne(string? raw, out string? tag, out string error)
    {
        tag = null;
        error = "invalid_tag";
        if (raw is null) return false;
        var trimmed = raw.Trim();
        if (trimmed.Length == 0) return false;
        if (trimmed.Length > MaxTagLength) return false;
        if (trimmed.Any(char.IsControl)) return false;
        // Letters, numbers, spaces, and a small separator set; Unicode letters/digits allowed.
        foreach (var ch in trimmed)
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' or '/' or '+' or ' ') continue;
            return false;
        }
        tag = trimmed.ToLowerInvariant();
        if (tag.Length == 0) return false;
        error = "";
        return true;
    }
}
