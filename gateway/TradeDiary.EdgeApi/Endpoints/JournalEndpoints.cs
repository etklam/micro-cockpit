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
    }
}
