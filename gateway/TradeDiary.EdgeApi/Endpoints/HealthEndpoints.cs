internal static class HealthEndpoints
{
    internal static void Map(WebApplication app)
    {
        app.MapGet("/health/live", () => Results.Ok(new HealthResponse("healthy"))).AllowAnonymous();
        app.MapGet("/health/ready", () => Results.Ok(new HealthResponse("ready"))).AllowAnonymous();
        app.MapGet("/version", () => Results.Ok(new VersionResponse("edge-api", "0.1.0"))).AllowAnonymous();
    }

    private sealed record HealthResponse(string Status);
    private sealed record VersionResponse(string Service, string Version);
}
