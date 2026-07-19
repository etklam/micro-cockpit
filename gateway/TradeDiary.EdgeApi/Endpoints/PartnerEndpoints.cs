using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Serialization;

internal static class PartnerEndpoints
{
    internal static void Map(WebApplication app)
    {
        EdgeTransport.MapProxy(app, "/api/app/partners", "partner", "/internal/partners", [HttpMethods.Get, HttpMethods.Post]);
        EdgeTransport.MapProxy(app, "/api/app/partners/{id:guid}", "partner", "/internal/partners/{id}", [HttpMethods.Delete]);
        EdgeTransport.MapProxy(app, "/api/app/partners/{id:guid}/accept", "partner", "/internal/partners/{id}/accept", [HttpMethods.Post]);
        EdgeTransport.MapProxy(app, "/api/app/partners/{id:guid}/share-policy", "partner", "/internal/partners/{id}/share-policy", [HttpMethods.Put]);
        EdgeTransport.MapProxy(app, "/api/app/partners/{ownerId:guid}/authorization", "partner", "/internal/partners/{ownerId}/authorization", [HttpMethods.Get]);
        EdgeTransport.MapProxy(app, "/api/app/partners/{id:guid}/summary", "partner", "/internal/partners/{id}/summary", [HttpMethods.Get]);
        EdgeTransport.MapProxy(app, "/api/app/partners/invitations", "partner", "/internal/partners/invitations", [HttpMethods.Get])
            .RequireRateLimiting(PartnerRateLimiting.InviteRead);
        EdgeTransport.MapProxy(app, "/api/app/partners/invitations", "partner", "/internal/partners/invitations", [HttpMethods.Post])
            .RequireRateLimiting(PartnerRateLimiting.InviteCreate);
        EdgeTransport.MapProxy(app, "/api/app/partners/invitations/{id:guid}", "partner", "/internal/partners/invitations/{id}", [HttpMethods.Delete]);
        EdgeTransport.MapProxy(app, "/api/app/partners/invitations/redeem", "partner", "/internal/partners/invitations/redeem", [HttpMethods.Post])
            .RequireRateLimiting(PartnerRateLimiting.InviteRedeem);

        app.MapGet("/api/app/partners/{linkId:guid}/compare", async (
            Guid linkId,
            DateOnly? from,
            DateOnly? to,
            HttpContext context,
            EdgeTransport transport,
            TimeProvider time) =>
        {
            var result = await PartnerCompareComposition.CompareAsync(linkId, from, to, transport, context, time);
            return CompositionResults.ToHttpResult(result, transport, context);
        });
    }
}

internal static class PartnerRateLimiting
{
    internal const string InviteCreate = "partner-invite-create";
    internal const string InviteRedeem = "partner-invite-redeem";
    internal const string InviteRead = "partner-invite-read";
}

internal static class PartnerCompareComposition
{
    internal static async Task<CompositionResult<PartnerCompareResponse>> CompareAsync(
        Guid linkId,
        DateOnly? from,
        DateOnly? to,
        EdgeTransport transport,
        HttpContext context,
        TimeProvider time)
    {
        var summary = await transport.GetAsync<PartnerLinkViewResponse>("partner", $"/internal/partners/{linkId:D}/summary", context);
        if (!summary.IsSuccess)
            return CompositionResult<PartnerCompareResponse>.Fail(new CompositionFailure(summary.StatusCode, summary.Failure));

        var link = summary.Value!;
        if (!string.Equals(link.Status, "accepted", StringComparison.Ordinal))
            return CompositionResult<PartnerCompareResponse>.Fail(new CompositionFailure(StatusCodes.Status404NotFound, DownstreamFailure.None));

        if (!TryResolveRange(from, to, context.User, time, out var rangeFrom, out var rangeTo, out var rangeError))
            return CompositionResult<PartnerCompareResponse>.Fail(new CompositionFailure(StatusCodes.Status400BadRequest, DownstreamFailure.None));

        var fromText = rangeFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toText = rangeTo.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var mineTask = LoadMyDiariesAsync(transport, context, fromText, toText);

        DownstreamResponse<PartnerDiaryCollectionResponse> partnerDiaries;
        PartnerDiaryCapability partnerCapability;
        if (link.PartnerShareDiaries)
        {
            partnerDiaries = await transport.GetAsync<PartnerDiaryCollectionResponse>(
                "journal",
                $"/internal/partner-diaries?ownerId={link.OtherUserId:D}&from={fromText}&to={toText}",
                context);
            if (partnerDiaries.StatusCode is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden)
                return CompositionResult<PartnerCompareResponse>.Fail(new CompositionFailure(partnerDiaries.StatusCode, partnerDiaries.Failure));
            if (partnerDiaries.IsSuccess)
                partnerCapability = PartnerDiaryCapability.Available;
            else if (partnerDiaries.StatusCode == StatusCodes.Status404NotFound)
                // Owner disabled sharing between list and read, or no longer authorized.
                partnerCapability = PartnerDiaryCapability.NotShared;
            else if (partnerDiaries.Failure != DownstreamFailure.None || partnerDiaries.StatusCode >= 500)
                partnerCapability = PartnerDiaryCapability.Unavailable;
            else
                return CompositionResult<PartnerCompareResponse>.Fail(new CompositionFailure(partnerDiaries.StatusCode, partnerDiaries.Failure));
        }
        else
        {
            partnerDiaries = new DownstreamResponse<PartnerDiaryCollectionResponse>(StatusCodes.Status200OK, new PartnerDiaryCollectionResponse([]));
            partnerCapability = PartnerDiaryCapability.NotShared;
        }

        var mine = await mineTask;
        if (mine.Failure is not null)
            return CompositionResult<PartnerCompareResponse>.Fail(mine.Failure);

        var mineItems = mine.Items;
        var partnerItems = partnerCapability == PartnerDiaryCapability.Available && partnerDiaries.Value?.Items is { } items
            ? items.Select(d => new PartnerCompareDiaryItem(d.Id, d.LocalDate, d.Title, d.Content, d.Tags)).ToList()
            : [];

        var dayMap = new SortedDictionary<DateOnly, DayBucket>(Comparer<DateOnly>.Create((a, b) => b.CompareTo(a)));
        foreach (var item in mineItems)
        {
            if (!dayMap.TryGetValue(item.LocalDate, out var bucket))
            {
                bucket = new DayBucket();
                dayMap[item.LocalDate] = bucket;
            }
            bucket.Mine.Add(item);
        }
        foreach (var item in partnerItems)
        {
            if (!dayMap.TryGetValue(item.LocalDate, out var bucket))
            {
                bucket = new DayBucket();
                dayMap[item.LocalDate] = bucket;
            }
            bucket.Partner.Add(item);
        }

        var days = dayMap.Select(pair => new PartnerCompareDayResponse(pair.Key, pair.Value.Mine, pair.Value.Partner)).ToList();
        return CompositionResult<PartnerCompareResponse>.Success(new PartnerCompareResponse(
            link.Id,
            link.PartnerDisplayName,
            link.OtherUserId,
            rangeFrom,
            rangeTo,
            days,
            new PartnerCompareCapabilitiesResponse(partnerCapability)));
    }

    private static bool TryResolveRange(
        DateOnly? from,
        DateOnly? to,
        ClaimsPrincipal user,
        TimeProvider time,
        out DateOnly rangeFrom,
        out DateOnly rangeTo,
        out string? error)
    {
        error = null;
        var today = CockpitComposition.ResolveLocalDate(user, time.GetUtcNow());
        rangeTo = to ?? today;
        rangeFrom = from ?? rangeTo.AddDays(-29);
        if (rangeTo < rangeFrom)
        {
            error = "invalid_date_range";
            return false;
        }
        if (rangeTo.DayNumber - rangeFrom.DayNumber > 366)
        {
            error = "range_too_large";
            return false;
        }
        return true;
    }

    // Journal list max is 100; page by cursor so a 366-day window still returns every entry.
    private static async Task<(List<PartnerCompareDiaryItem> Items, CompositionFailure? Failure)> LoadMyDiariesAsync(
        EdgeTransport transport,
        HttpContext context,
        string fromText,
        string toText)
    {
        var items = new List<PartnerCompareDiaryItem>();
        string? cursor = null;
        for (var page = 0; page < 50; page++)
        {
            var path = $"/internal/diaries?from={fromText}&to={toText}&limit=100" +
                       (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var response = await transport.GetAsync<DiaryPageResponse>("journal", path, context);
            if (!response.IsSuccess)
                return ([], new CompositionFailure(response.StatusCode, response.Failure));
            items.AddRange(response.Value!.Items.Select(d =>
                new PartnerCompareDiaryItem(d.Id, d.LocalDate, d.Title, d.Content, d.Tags)));
            cursor = response.Value.NextCursor;
            if (string.IsNullOrEmpty(cursor)) break;
        }
        return (items, null);
    }

    private sealed class DayBucket
    {
        public List<PartnerCompareDiaryItem> Mine { get; } = [];
        public List<PartnerCompareDiaryItem> Partner { get; } = [];
    }
}

public enum PartnerDiaryCapability
{
    Available,
    [JsonStringEnumMemberName("not_shared")]
    NotShared,
    Unavailable
}

public sealed record PartnerCompareResponse(
    Guid LinkId,
    string PartnerDisplayName,
    Guid PartnerUserId,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<PartnerCompareDayResponse> Days,
    PartnerCompareCapabilitiesResponse Capabilities);

public sealed record PartnerCompareDayResponse(
    DateOnly LocalDate,
    IReadOnlyList<PartnerCompareDiaryItem> Mine,
    IReadOnlyList<PartnerCompareDiaryItem> Partner);

public sealed record PartnerCompareDiaryItem(
    Guid Id,
    DateOnly LocalDate,
    string Title,
    string Content,
    IReadOnlyList<string> Tags);

public sealed record PartnerCompareCapabilitiesResponse(PartnerDiaryCapability PartnerDiaries);

// Partner service DTOs used by Edge composition / proxies (schema names must match service OpenAPI).
internal sealed record PartnerLinkViewResponse(
    Guid Id,
    Guid OtherUserId,
    string PartnerType,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool InitiatedByMe,
    bool MyShareDiaries,
    bool PartnerShareDiaries,
    string PartnerDisplayName);

internal sealed record PartnerDiaryCollectionResponse(IReadOnlyList<PartnerDiaryItemResponse> Items);
internal sealed record PartnerDiaryItemResponse(Guid Id, DateOnly LocalDate, string Title, string Content, IReadOnlyList<string> Tags);
