internal static class AdminEndpoints
{
    internal static void Map(WebApplication app)
    {
        EdgeTransport.MapProxy(app, "/api/app/tools/position-sizing", "tool", "/internal/tools/position-sizing", [HttpMethods.Post]);
        EdgeTransport.MapProxy(app, "/api/app/tools/risk-reward", "tool", "/internal/tools/risk-reward", [HttpMethods.Post]);
        EdgeTransport.MapProxy(app, "/api/app/tools/average-cost", "tool", "/internal/tools/average-cost", [HttpMethods.Post]);
        EdgeTransport.MapProxy(app, "/api/app/tools/profit-loss", "tool", "/internal/tools/profit-loss", [HttpMethods.Post]);
        EdgeTransport.MapProxy(app, "/api/app/tool-presets", "tool", "/internal/tool-presets", [HttpMethods.Get, HttpMethods.Post]);
        EdgeTransport.MapProxy(app, "/api/app/tool-presets/{id:guid}", "tool", "/internal/tool-presets/{id}", [HttpMethods.Put, HttpMethods.Delete]);
        EdgeTransport.MapProxy(app, "/api/app/tool-presets/{id:guid}/use", "tool", "/internal/tool-presets/{id}/use", [HttpMethods.Post]);
        EdgeTransport.MapProxy(app, "/api/app/saved-calculations", "tool", "/internal/saved-calculations", [HttpMethods.Get, HttpMethods.Post]);
        EdgeTransport.MapProxy(app, "/api/app/saved-calculations/{id:guid}", "tool", "/internal/saved-calculations/{id}", [HttpMethods.Delete]);
        EdgeTransport.MapProxy(app, "/api/admin/posts", "content", "/internal/admin/posts", [HttpMethods.Post]).RequireAuthorization("admin");
        EdgeTransport.MapProxy(app, "/api/admin/posts/{id:guid}", "content", "/internal/admin/posts/{id}", [HttpMethods.Put, HttpMethods.Delete]).RequireAuthorization("admin");
        EdgeTransport.MapProxy(app, "/api/admin/operations/audit", "operations", "/internal/operations/audit", [HttpMethods.Get]).RequireAuthorization("admin");
        EdgeTransport.MapProxy(app, "/api/admin/operations/jobs", "operations", "/internal/operations/jobs", [HttpMethods.Get, HttpMethods.Post]).RequireAuthorization("admin");
        EdgeTransport.MapProxy(app, "/api/admin/operations/health", "operations", "/internal/operations/health", [HttpMethods.Post]).RequireAuthorization("admin");
        EdgeTransport.MapProxy(app, "/api/content/posts", "content", "/internal/posts", [HttpMethods.Get]).AllowAnonymous();
        EdgeTransport.MapProxy(app, "/api/content/posts/{slug}", "content", "/internal/posts/{slug}", [HttpMethods.Get]).AllowAnonymous();
    }
}
