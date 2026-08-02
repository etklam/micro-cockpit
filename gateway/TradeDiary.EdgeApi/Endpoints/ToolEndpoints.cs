internal static class ToolEndpoints
{
    internal static void Map(WebApplication app)
    {
        EdgeTransport.MapProxy(app, "/api/app/tools/position-sizing", "tool", "/internal/tools/position-sizing", [HttpMethods.Post], preserveErrorBody: true);
        EdgeTransport.MapProxy(app, "/api/app/tools/risk-reward", "tool", "/internal/tools/risk-reward", [HttpMethods.Post], preserveErrorBody: true);
        EdgeTransport.MapProxy(app, "/api/app/tools/average-cost", "tool", "/internal/tools/average-cost", [HttpMethods.Post], preserveErrorBody: true);
        EdgeTransport.MapProxy(app, "/api/app/tools/profit-loss", "tool", "/internal/tools/profit-loss", [HttpMethods.Post], preserveErrorBody: true);

        EdgeTransport.MapProxy(app, "/api/app/tool-presets", "tool", "/internal/tool-presets", [HttpMethods.Get, HttpMethods.Post], preserveErrorBody: true);
        EdgeTransport.MapProxy(app, "/api/app/tool-presets/{id:guid}", "tool", "/internal/tool-presets/{id}", [HttpMethods.Put, HttpMethods.Delete], preserveErrorBody: true);
        EdgeTransport.MapProxy(app, "/api/app/tool-presets/{id:guid}/use", "tool", "/internal/tool-presets/{id}/use", [HttpMethods.Post], preserveErrorBody: true);
        EdgeTransport.MapProxy(app, "/api/app/saved-calculations", "tool", "/internal/saved-calculations", [HttpMethods.Get, HttpMethods.Post], preserveErrorBody: true);
        EdgeTransport.MapProxy(app, "/api/app/saved-calculations/{id:guid}", "tool", "/internal/saved-calculations/{id}", [HttpMethods.Delete], preserveErrorBody: true);
    }
}
