using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;

static class ObservationEnrichment
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    internal static string? Normalize(ObservationUpdateWrite input, out ObservationEnrichmentValue value)
    {
        value = default!;
        if (string.IsNullOrWhiteSpace(input.Content)) return "content_required";
        if (input.Content.Trim().Length > 10_000) return "content_too_long";
        if (Trim(input.Signal)?.Length > 10_000 || Trim(input.Interpretation)?.Length > 10_000) return "enrichment_too_long";
        if (Trim(input.MentalState)?.Length > 100) return "mental_state_too_long";

        var tagError = DiaryTags.NormalizeAll(input.Tags, out var normalizedTags);
        if (tagError is not null) return tagError;
        normalizedTags = normalizedTags.Order(StringComparer.Ordinal).ToArray();

        var subjectError = NormalizeSubject(input.PrimarySubject, out var primary);
        if (subjectError is not null) return subjectError;
        if ((input.RelatedSubjects?.Count ?? 0) > 10) return "too_many_related_subjects";
        var related = new List<ObservationSubjectResponse>();
        foreach (var subject in input.RelatedSubjects ?? [])
        {
            subjectError = NormalizeSubject(subject, out var normalized);
            if (subjectError is not null) return subjectError;
            if (normalized is not null) related.Add(normalized);
        }

        if (input.Evidence is not null && Trim(input.Signal) is null) return "evidence_requires_signal";
        var evidenceError = NormalizeEvidence(input.Evidence, out var evidence);
        if (evidenceError is not null) return evidenceError;
        value = new ObservationEnrichmentValue(
            input.Content.Trim(), Trim(input.Signal), Trim(input.Interpretation), Trim(input.MentalState),
            normalizedTags, primary, related, evidence);
        return null;
    }

    private static string? NormalizeSubject(ObservationSubjectWrite? input, out ObservationSubjectResponse? subject)
    {
        subject = null;
        if (input is null) return null;
        var type = input.Type;
        if (type != ObservationSubjectType.instrument)
        {
            var name = Trim(input.Name);
            if (name is null || name.Length > 120) return "subject_name_required";
            if (input.InstrumentId is not null || Trim(input.Market) is not null || Trim(input.Symbol) is not null || Trim(input.DisplayName) is not null)
                return "invalid_subject_fields";
            subject = new(type, name, null, null, null, null, false);
            return null;
        }

        var market = Trim(input.Market)?.ToUpperInvariant();
        var symbol = Trim(input.Symbol)?.ToUpperInvariant();
        var displayName = Trim(input.DisplayName);
        if (market is null || symbol is null || displayName is null || market.Length > 32 || symbol.Length > 24 || displayName.Length > 160)
            return "instrument_fields_required";
        if (market == "US" && input.InstrumentId is null) return "directory_instrument_required";
        if (market != "US" && input.InstrumentId is not null) return "manual_instrument_required";
        subject = new(type, null, input.InstrumentId, market, symbol, displayName, market == "US");
        return null;
    }

    private static string? NormalizeEvidence(ObservationEvidenceWrite? input, out ObservationEvidenceResponse? evidence)
    {
        evidence = null;
        if (input is null) return null;
        if (!Uri.TryCreate(Trim(input.Url), UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https")) return "invalid_evidence_url";
        var title = Trim(input.Title);
        var quote = Trim(input.Quote);
        if (url.AbsoluteUri.Length > 2048 || title?.Length > 300 || quote?.Length > 2_000) return "evidence_too_long";
        evidence = new(url.AbsoluteUri, title, quote);
        return null;
    }

    internal static ObservationUpdateResponse Read(NpgsqlDataReader reader, int offset = 0) => new(
        reader.GetGuid(offset), reader.GetString(offset + 1), reader.GetDateTime(offset + 2), reader.GetDateTime(offset + 3),
        reader.IsDBNull(offset + 4) ? null : reader.GetString(offset + 4), reader.IsDBNull(offset + 5) ? null : reader.GetString(offset + 5),
        reader.IsDBNull(offset + 6) ? null : reader.GetString(offset + 6), reader.GetFieldValue<string[]>(offset + 7),
        ReadJson<ObservationSubjectResponse>(reader.IsDBNull(offset + 8) ? null : reader.GetString(offset + 8)),
        ReadJson<List<ObservationSubjectResponse>>(reader.GetString(offset + 9)) ?? [],
        ReadJson<ObservationEvidenceResponse>(reader.IsDBNull(offset + 10) ? null : reader.GetString(offset + 10)));

    internal static ObservationUpdateEditResponse ReadEdit(NpgsqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetString(1), reader.GetDateTime(2), reader.GetDateTime(3), true,
        reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetFieldValue<string[]>(7),
        ReadJson<ObservationSubjectResponse>(reader.IsDBNull(8) ? null : reader.GetString(8)),
        ReadJson<List<ObservationSubjectResponse>>(reader.GetString(9)) ?? [],
        ReadJson<ObservationEvidenceResponse>(reader.IsDBNull(10) ? null : reader.GetString(10)));

    internal static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    internal static string? Json<T>(T? value) where T : class => value is null ? null : JsonSerializer.Serialize(value, JsonOptions);
    private static T? ReadJson<T>(string? value) => value is null ? default : JsonSerializer.Deserialize<T>(value, JsonOptions);
    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

sealed record ObservationEnrichmentValue(
    string Content,
    string? Signal,
    string? Interpretation,
    string? MentalState,
    IReadOnlyList<string> Tags,
    ObservationSubjectResponse? PrimarySubject,
    IReadOnlyList<ObservationSubjectResponse> RelatedSubjects,
    ObservationEvidenceResponse? Evidence);
