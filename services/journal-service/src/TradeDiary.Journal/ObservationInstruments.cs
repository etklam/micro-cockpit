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

    internal static async Task<ObservationUpdateResponse> AttachDailyCloseAsync(
        IHttpClientFactory factory,
        ObservationUpdateResponse update,
        DateOnly onOrBefore,
        CancellationToken cancellationToken)
    {
        var client = factory.CreateClient("market-data");
        var primary = await Attach(client, update.PrimarySubject, onOrBefore, cancellationToken);
        var related = new List<ObservationSubjectResponse>();
        foreach (var subject in update.RelatedSubjects)
            related.Add((await Attach(client, subject, onOrBefore, cancellationToken))!);
        return update with { PrimarySubject = primary, RelatedSubjects = related };
    }

    private static async Task<ObservationSubjectResponse?> Attach(
        HttpClient client,
        ObservationSubjectResponse? subject,
        DateOnly onOrBefore,
        CancellationToken cancellationToken)
    {
        if (subject is null) return null;
        if (!subject.DailyCloseAvailable || subject.InstrumentId is null)
            return subject with { DailyCloseStatus = DailyCloseStatus.unsupported, DailyClose = null };
        try
        {
            using var response = await client.GetAsync(
                $"/internal/v1/instruments/{subject.InstrumentId}/daily-close?onOrBefore={onOrBefore:yyyy-MM-dd}",
                cancellationToken);
            if (!response.IsSuccessStatusCode)
                return subject with { DailyCloseStatus = DailyCloseStatus.unavailable, DailyClose = null };
            var close = await response.Content.ReadFromJsonAsync<PublishedDailyClose>(cancellationToken);
            if (close is null || close.Status != "available" || close.TradingDate is null
                || close.RawClose is null || close.AdjustedClose is null
                || close.Provider is null || close.PublishedAt is null)
                return subject with { DailyCloseStatus = DailyCloseStatus.unavailable, DailyClose = null };
            return subject with
            {
                DailyCloseStatus = DailyCloseStatus.available,
                DailyClose = new(
                    close.TradingDate.Value, close.RawClose.Value, close.AdjustedClose.Value,
                    close.Provider, close.PublishedAt.Value),
            };
        }
        catch (HttpRequestException)
        {
            return subject with { DailyCloseStatus = DailyCloseStatus.unavailable, DailyClose = null };
        }
    }

    private sealed record PublishedInstrument(Guid InstrumentId, string Symbol, string Name, string Exchange, string Currency, string Timezone);
    private sealed record PublishedDailyClose(
        Guid InstrumentId,
        string Symbol,
        string Status,
        DateOnly? TradingDate,
        decimal? RawClose,
        decimal? AdjustedClose,
        string? Provider,
        DateTime? PublishedAt);
    private sealed record SubjectResolution(ObservationSubjectResponse? Subject, string? Error, int StatusCode);
}

sealed record InstrumentResolution(ObservationEnrichmentValue? Value, string? Error, int StatusCode);
