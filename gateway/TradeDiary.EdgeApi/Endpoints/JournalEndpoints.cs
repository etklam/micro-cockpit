internal static class JournalEndpoints
{
    internal static void Map(WebApplication app)
    {
        EdgeTransport.MapProxy(app, "/api/app/quick-note", "journal", "/internal/quick-note", [HttpMethods.Post]).RequireAuthorization("diaryAccess");
        EdgeTransport.MapProxy(app, "/api/app/diaries", "journal", "/internal/diaries", [HttpMethods.Get, HttpMethods.Post]).RequireAuthorization("diaryAccess");
        EdgeTransport.MapProxy(app, "/api/app/diaries/{id:guid}", "journal", "/internal/diaries/{id}", [HttpMethods.Get, HttpMethods.Put, HttpMethods.Delete]).RequireAuthorization("diaryAccess");
        EdgeTransport.MapProxy(app, "/api/app/diaries/{diaryId:guid}/transactions", "journal", "/internal/diaries/{diaryId}/transactions", [HttpMethods.Get, HttpMethods.Post]).RequireAuthorization("diaryAccess");
        EdgeTransport.MapProxy(app, "/api/app/diaries/{diaryId:guid}/transactions/{id:guid}", "journal", "/internal/diaries/{diaryId}/transactions/{id}", [HttpMethods.Put, HttpMethods.Delete]).RequireAuthorization("diaryAccess");
    }
}
