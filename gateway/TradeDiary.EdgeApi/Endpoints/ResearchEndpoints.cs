internal static class ResearchEndpoints
{
    internal static void Map(WebApplication app)
    {
        EdgeTransport.MapProxy(app, "/api/app/market/symbols", "market-data", "/internal/v1/symbols", [HttpMethods.Get]);
        EdgeTransport.MapProxy(app, "/api/app/market/bars/{symbol}", "market-data", "/internal/v1/bars/{symbol}", [HttpMethods.Get]);
        EdgeTransport.MapProxy(app, "/api/app/market/providers/health", "market-data", "/internal/v1/providers/health", [HttpMethods.Get]);
    }
}
