using System.Net;
using System.Net.Http.Json;

static class ObservationInstruments
{
    internal static async Task<InstrumentResolution> ResolveAsync(IHttpClientFactory factory, ObservationEnrichmentValue value, CancellationToken cancellationToken)
    {
        var client = factory.CreateClient("market-data");
        var primary = await ResolveSubject(client, value.PrimarySubject, cancellationToken);
        if (primary.Error is not null) return new(null, primary.Error, primary.StatusCode);
        var related = new List<ObservationSubjectResponse>();
        foreach (var subject in value.RelatedSubjects)
        {
            var resolved = await ResolveSubject(client, subject, cancellationToken);
            if (resolved.Error is not null) return new(null, resolved.Error, resolved.StatusCode);
            related.Add(resolved.Subject!);
        }
        return new(value with { PrimarySubject = primary.Subject, RelatedSubjects = related }, null, 200);
    }

    private static async Task<SubjectResolution> ResolveSubject(HttpClient client, ObservationSubjectResponse? subject, CancellationToken cancellationToken)
    {
        if (subject is null || !subject.DailyCloseAvailable) return new(subject, null, 200);
        try
        {
            using var response = await client.GetAsync($"/internal/v1/instruments/{subject.InstrumentId}", cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound) return new(null, "unknown_instrument", 400);
            if (!response.IsSuccessStatusCode) return new(null, "instrument_directory_unavailable", 503);
            var instrument = await response.Content.ReadFromJsonAsync<PublishedInstrument>(cancellationToken);
            if (instrument is null || instrument.InstrumentId != subject.InstrumentId) return new(null, "unknown_instrument", 400);
            return new(subject with { Market = "US", Symbol = instrument.Symbol, DisplayName = instrument.Name }, null, 200);
        }
        catch (HttpRequestException)
        {
            return new(null, "instrument_directory_unavailable", 503);
        }
    }

    private sealed record PublishedInstrument(Guid InstrumentId, string Symbol, string Name, string Exchange, string Currency, string Timezone);
    private sealed record SubjectResolution(ObservationSubjectResponse? Subject, string? Error, int StatusCode);
}

sealed record InstrumentResolution(ObservationEnrichmentValue? Value, string? Error, int StatusCode);
