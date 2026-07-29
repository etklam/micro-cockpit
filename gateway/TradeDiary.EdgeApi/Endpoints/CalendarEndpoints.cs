internal static class CalendarEndpoints
{
    internal static void Map(WebApplication app) =>
        app.MapGet("/api/app/calendar", async (
            int year,
            int month,
            HttpContext context,
            EdgeTransport transport) =>
        {
            if (!CockpitComposition.TryCreateCalendarWindow(year, month, out var window))
                return EdgeProblems.InvalidRequest(context, "The calendar month is invalid.");
            var result = await CockpitComposition.CalendarAsync(window, transport, context);
            if (result.Failure is null) return Results.Ok(result.Value);
            return transport.ProblemFor(
                new DownstreamResponse<CalendarResponse>(
                    result.Failure.StatusCode, default, result.Failure.Failure),
                context);
        });
}
