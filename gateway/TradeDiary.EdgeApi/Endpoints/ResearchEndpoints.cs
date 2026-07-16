internal static class ResearchEndpoints
{
    internal static void Map(WebApplication app)
    {
        EdgeTransport.MapProxy(app, "/api/app/stocks", "stock-research", "/internal/stocks", [HttpMethods.Get, HttpMethods.Post]).RequireAuthorization("researchAccess");
        EdgeTransport.MapProxy(app, "/api/app/stocks/{symbol}", "stock-research", "/internal/stocks/{symbol}", [HttpMethods.Get]).RequireAuthorization("researchAccess");
        EdgeTransport.MapProxy(app, "/api/app/watchlist", "stock-research", "/internal/watchlist", [HttpMethods.Get]).RequireAuthorization("researchAccess");
        EdgeTransport.MapProxy(app, "/api/app/watchlist/{stockId:guid}", "stock-research", "/internal/watchlist/{stockId}", [HttpMethods.Post, HttpMethods.Delete]).RequireAuthorization("researchAccess");
        EdgeTransport.MapProxy(app, "/api/app/stocks/{stockId:guid}/note", "stock-research", "/internal/stocks/{stockId}/note", [HttpMethods.Get, HttpMethods.Put]).RequireAuthorization("researchAccess");
        EdgeTransport.MapProxy(app, "/api/app/stocks/{stockId:guid}/timeline", "stock-research", "/internal/stocks/{stockId}/timeline", [HttpMethods.Get, HttpMethods.Post]).RequireAuthorization("researchAccess");
        EdgeTransport.MapProxy(app, "/api/app/timeline/{id:guid}", "stock-research", "/internal/timeline/{id}", [HttpMethods.Get]).RequireAuthorization("researchAccess");
        EdgeTransport.MapProxy(app, "/api/app/timeline/{originalId:guid}/corrections", "stock-research", "/internal/timeline/{originalId}/corrections", [HttpMethods.Post]).RequireAuthorization("researchAccess");
        EdgeTransport.MapProxy(app, "/api/app/market/symbols", "market-data", "/internal/v1/symbols", [HttpMethods.Get]);
        EdgeTransport.MapProxy(app, "/api/app/market/bars/{symbol}", "market-data", "/internal/v1/bars/{symbol}", [HttpMethods.Get]);
        EdgeTransport.MapProxy(app, "/api/app/market/providers/health", "market-data", "/internal/v1/providers/health", [HttpMethods.Get]);
        EdgeTransport.MapProxy(app, "/api/app/price-alerts", "price-alert", "/internal/price-alerts", [HttpMethods.Get, HttpMethods.Post]);
        EdgeTransport.MapProxy(app, "/api/app/price-alerts/{id:guid}", "price-alert", "/internal/price-alerts/{id}", [HttpMethods.Put, HttpMethods.Delete]);
        EdgeTransport.MapProxy(app, "/api/app/price-alerts/{id:guid}/dismiss", "price-alert", "/internal/price-alerts/{id}/dismiss", [HttpMethods.Post]);
        EdgeTransport.MapProxy(app, "/api/app/price-alerts/{id:guid}/reactivate", "price-alert", "/internal/price-alerts/{id}/reactivate", [HttpMethods.Post]);
        EdgeTransport.MapProxy(app, "/api/app/price-alerts/{id:guid}/triggers", "price-alert", "/internal/price-alerts/{id}/triggers", [HttpMethods.Get]);
        EdgeTransport.MapProxy(app, "/api/app/rotation/universes", "rotation", "/internal/rotation/universes", [HttpMethods.Get, HttpMethods.Post]);
        EdgeTransport.MapProxy(app, "/api/app/rotation/universes/{id:guid}", "rotation", "/internal/rotation/universes/{id}", [HttpMethods.Put, HttpMethods.Delete]);
        EdgeTransport.MapProxy(app, "/api/app/rotation/universes/{id:guid}/symbols", "rotation", "/internal/rotation/universes/{id}/symbols", [HttpMethods.Put]);
        EdgeTransport.MapProxy(app, "/api/app/rotation/universes/{id:guid}/calculate", "rotation", "/internal/rotation/universes/{id}/calculate", [HttpMethods.Post]);
        app.MapGet("/api/app/rotation/monitor", async (string universe, DateOnly? date, HttpContext context, EdgeTransport transport) =>
        {
            var target = $"/internal/rotation/monitor?universe={Uri.EscapeDataString(universe)}" + (date is null ? "" : $"&date={date:yyyy-MM-dd}");
            var response = await transport.GetAsync<RotationMonitorResponse>("rotation", target, context);
            if (!response.IsSuccess) return transport.ProblemFor(response, context);
            var value = response.Value;
            if (value is null || value.Universe is null || value.MarketState is null || value.SectorBreadth is null || value.Etfs is null ||
                string.IsNullOrWhiteSpace(value.FormulaVersion) || string.IsNullOrWhiteSpace(value.Status))
                return EdgeProblems.DownstreamInvalid(context);
            return Results.Ok(value);
        });
    }
}
