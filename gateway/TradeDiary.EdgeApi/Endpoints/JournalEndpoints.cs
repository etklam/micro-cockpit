internal static class JournalEndpoints
{
    internal static void Map(WebApplication app)
    {
        EdgeTransport.MapProxy(app, "/api/app/quick-note", "journal", "/internal/quick-note", [HttpMethods.Post]).RequireAuthorization("diaryAccess");
        EdgeTransport.MapProxy(app, "/api/app/diaries", "journal", "/internal/diaries", [HttpMethods.Get, HttpMethods.Post]).RequireAuthorization("diaryAccess");
        EdgeTransport.MapProxy(app, "/api/app/diaries/{id:guid}", "journal", "/internal/diaries/{id}", [HttpMethods.Get, HttpMethods.Put, HttpMethods.Delete]).RequireAuthorization("diaryAccess");
        EdgeTransport.MapProxy(app, "/api/app/diaries/{diaryId:guid}/transactions", "journal", "/internal/diaries/{diaryId}/transactions", [HttpMethods.Get, HttpMethods.Post]).RequireAuthorization("diaryAccess");
        EdgeTransport.MapProxy(app, "/api/app/diaries/{diaryId:guid}/transactions/{id:guid}", "journal", "/internal/diaries/{diaryId}/transactions/{id}", [HttpMethods.Put, HttpMethods.Delete]).RequireAuthorization("diaryAccess");
        app.MapGet("/api/app/diaries/{diaryId:guid}/review", async (Guid diaryId, HttpContext context, EdgeTransport transport) =>
        {
            var response = await transport.GetAsync<DiaryReviewResponse>("journal", $"/internal/diaries/{diaryId}/review", context);
            return response.IsSuccess ? Results.Ok(response.Value) : transport.ProblemFor(response, context);
        }).RequireAuthorization("diaryAccess");
        app.MapPut("/api/app/diaries/{diaryId:guid}/review", async (Guid diaryId, DiaryReviewWrite input, HttpContext context, EdgeTransport transport) =>
        {
            var response = await transport.SendJsonAsync<DiaryReviewWrite, DiaryReviewResponse>("journal", $"/internal/diaries/{diaryId}/review", HttpMethod.Put, input, context);
            return response.IsSuccess ? Results.Ok(response.Value) : transport.ProblemFor(response, context);
        }).RequireAuthorization("diaryAccess");
        EdgeTransport.MapProxy(app, "/api/app/diaries/{diaryId:guid}/review", "journal", "/internal/diaries/{diaryId}/review", [HttpMethods.Delete]).RequireAuthorization("diaryAccess");
        app.MapGet("/api/app/diary-review-summary", async (DateOnly from, DateOnly to, HttpContext context, EdgeTransport transport) =>
        {
            var response = await transport.GetAsync<DiaryReviewSummaryResponse>("journal", $"/internal/diary-review-summary?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}", context);
            return response.IsSuccess ? Results.Ok(response.Value) : transport.ProblemFor(response, context);
        }).RequireAuthorization("diaryAccess");
        app.MapGet("/api/app/diary-review-items", async (
            HttpContext context,
            EdgeTransport transport,
            DateOnly from,
            DateOnly to,
            DiaryReviewFilterStatus status = DiaryReviewFilterStatus.all,
            DiaryReviewAssessmentFilter assessment = DiaryReviewAssessmentFilter.all,
            string? tag = null,
            string? cursor = null,
            int limit = 50) =>
        {
            if (to < from || to.DayNumber - from.DayNumber >= 62 || limit is < 1 or > 100 || !ValidTag(tag) || !ValidCursor(cursor))
                return EdgeProblems.InvalidRequest(context);
            var query = new List<string>
            {
                $"from={from:yyyy-MM-dd}", $"to={to:yyyy-MM-dd}",
                $"status={status.ToString().ToLowerInvariant()}", $"assessment={assessment.ToString().ToLowerInvariant()}",
                $"limit={limit}"
            };
            if (tag is not null) query.Add($"tag={Uri.EscapeDataString(tag)}");
            if (cursor is not null) query.Add($"cursor={Uri.EscapeDataString(cursor)}");
            var response = await transport.GetAsync<DiaryReviewItemsResponse>("journal", $"/internal/diary-review-items?{string.Join('&', query)}", context);
            return response.IsSuccess ? Results.Ok(response.Value) : transport.ProblemFor(response, context);
        }).RequireAuthorization("diaryAccess");
    }

    private static bool ValidTag(string? tag) => tag is null or "no_plan" or "fomo" or "poor_timing" or "risk_violation" or "overtrading" or "ignored_signal" or "early_exit" or "late_exit" or "other";

    private static bool ValidCursor(string? cursor)
    {
        if (cursor is null) return true;
        if (cursor.Length is 0 or > 2048 || cursor.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_')) return false;
        try
        {
            var encoded = cursor.Replace('-', '+').Replace('_', '/');
            encoded = encoded.PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '=');
            using var document = System.Text.Json.JsonDocument.Parse(Convert.FromBase64String(encoded));
            var root = document.RootElement;
            return root.TryGetProperty("localDate", out var date) && DateOnly.TryParse(date.GetString(), out _)
                && root.TryGetProperty("createdAt", out var created) && DateTime.TryParse(created.GetString(), out _)
                && root.TryGetProperty("id", out var id) && Guid.TryParse(id.GetString(), out var parsedId) && parsedId != Guid.Empty;
        }
        catch (Exception error) when (error is FormatException or System.Text.Json.JsonException) { return false; }
    }
}
