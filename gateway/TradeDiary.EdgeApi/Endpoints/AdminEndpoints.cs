internal static class AdminEndpoints
{
    internal static void Map(WebApplication app)
    {
        EdgeTransport.MapProxy(app, "/api/app/partners", "partner", "/internal/partners", [HttpMethods.Get, HttpMethods.Post]);
        EdgeTransport.MapProxy(app, "/api/app/partners/{id:guid}", "partner", "/internal/partners/{id}", [HttpMethods.Delete]);
        EdgeTransport.MapProxy(app, "/api/app/partners/{id:guid}/accept", "partner", "/internal/partners/{id}/accept", [HttpMethods.Post]);
        EdgeTransport.MapProxy(app, "/api/app/partners/{id:guid}/share-policy", "partner", "/internal/partners/{id}/share-policy", [HttpMethods.Put]);
        EdgeTransport.MapProxy(app, "/api/app/partners/{ownerId:guid}/authorization", "partner", "/internal/partners/{ownerId}/authorization", [HttpMethods.Get]);
        EdgeTransport.MapProxy(app, "/api/app/tools/position-sizing", "tool", "/internal/tools/position-sizing", [HttpMethods.Post]);
        EdgeTransport.MapProxy(app, "/api/app/tools/risk-reward", "tool", "/internal/tools/risk-reward", [HttpMethods.Post]);
        EdgeTransport.MapProxy(app, "/api/app/tools/fire", "tool", "/internal/tools/fire", [HttpMethods.Post]);
        EdgeTransport.MapProxy(app, "/api/app/tools/relative-value", "tool", "/internal/tools/relative-value", [HttpMethods.Post]);
        EdgeTransport.MapProxy(app, "/api/app/tools/seasonality", "tool", "/internal/tools/seasonality", [HttpMethods.Post]);
        EdgeTransport.MapProxy(app, "/api/admin/posts", "content", "/internal/admin/posts", [HttpMethods.Post]).RequireAuthorization("admin");
        EdgeTransport.MapProxy(app, "/api/admin/posts/{id:guid}", "content", "/internal/admin/posts/{id}", [HttpMethods.Put, HttpMethods.Delete]).RequireAuthorization("admin");
        EdgeTransport.MapProxy(app, "/api/admin/operations/audit", "operations", "/internal/operations/audit", [HttpMethods.Get]).RequireAuthorization("admin");
        EdgeTransport.MapProxy(app, "/api/admin/operations/jobs", "operations", "/internal/operations/jobs", [HttpMethods.Get, HttpMethods.Post]).RequireAuthorization("admin");
        EdgeTransport.MapProxy(app, "/api/admin/operations/health", "operations", "/internal/operations/health", [HttpMethods.Post]).RequireAuthorization("admin");
        EdgeTransport.MapProxy(app, "/api/content/posts", "content", "/internal/posts", [HttpMethods.Get]).AllowAnonymous();
        EdgeTransport.MapProxy(app, "/api/content/posts/{slug}", "content", "/internal/posts/{slug}", [HttpMethods.Get]).AllowAnonymous();
    }
}
