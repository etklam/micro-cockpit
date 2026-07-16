internal static class PerformanceEndpoints
{
    internal static void Map(WebApplication app) =>
        EdgeTransport.MapProxy(app, "/api/app/daily-performance/{date}", "performance", "/internal/daily-performances/{date}", [HttpMethods.Put]);
}

internal static class DisciplineEndpoints
{
    internal static void Map(WebApplication app)
    {
        EdgeTransport.MapProxy(app, "/api/app/disciplines", "discipline", "/internal/disciplines", [HttpMethods.Get, HttpMethods.Post]);
        EdgeTransport.MapProxy(app, "/api/app/disciplines/{id:guid}", "discipline", "/internal/disciplines/{id}", [HttpMethods.Put, HttpMethods.Delete]);
    }
}

internal static class ReminderEndpoints
{
    internal static void Map(WebApplication app)
    {
        EdgeTransport.MapProxy(app, "/api/app/diary-alerts", "reminder", "/internal/diary-alerts", [HttpMethods.Get, HttpMethods.Post]);
        EdgeTransport.MapProxy(app, "/api/app/diary-alerts/{id:guid}", "reminder", "/internal/diary-alerts/{id}", [HttpMethods.Delete]);
        EdgeTransport.MapProxy(app, "/api/app/diary-alerts/{id:guid}/dismiss", "reminder", "/internal/diary-alerts/{id}/dismiss", [HttpMethods.Post]);
    }
}
