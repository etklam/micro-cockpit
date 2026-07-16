internal static class DashboardEndpoints
{
    internal static void Map(WebApplication app) =>
        app.MapGet("/api/app/dashboard", async (HttpContext context, EdgeTransport transport, TimeProvider time) =>
        {
            var result = await CockpitComposition.DashboardAsync(
                CockpitComposition.ResolveLocalDate(context.User, time.GetUtcNow()), transport, context);
            return CompositionResults.ToHttpResult(result, transport, context);
        });
}

internal static class CalendarEndpoints
{
    internal static void Map(WebApplication app) =>
        app.MapGet("/api/app/calendar", async (int year, int month, HttpContext context, EdgeTransport transport) =>
        {
            if (!CockpitComposition.TryCreateCalendarWindow(year, month, out var window))
                return EdgeProblems.InvalidRequest(context, "The calendar month is invalid.");
            var result = await CockpitComposition.CalendarAsync(window, transport, context);
            return CompositionResults.ToHttpResult(result, transport, context);
        });
}

internal static class CompositionEndpoints
{
    internal static void Map(WebApplication app) =>
        app.MapGet("/api/app/stocks/{symbol}/page", async (string symbol, HttpContext context, EdgeTransport transport) =>
        {
            var result = await CockpitComposition.StockPageAsync(symbol, transport, context);
            return CompositionResults.ToHttpResult(result, transport, context);
        }).RequireAuthorization("researchAccess");
}

internal static class CompositionResults
{
    internal static IResult ToHttpResult<T>(CompositionResult<T> result, EdgeTransport transport, HttpContext context)
    {
        if (result.Failure is null) return Results.Ok(result.Value);
        return transport.ProblemFor(
            new DownstreamResponse<T>(result.Failure.StatusCode, default, result.Failure.Failure), context);
    }
}
