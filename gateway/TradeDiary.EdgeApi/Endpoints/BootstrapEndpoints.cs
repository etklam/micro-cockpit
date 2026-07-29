internal static class BootstrapEndpoints
{
    private static readonly string[] ProductAreas =
    [
        "today", "review", "watchlist", "calendar", "tools", "settings"
    ];

    internal static void Map(WebApplication app)
    {
        app.MapGet("/api/app/bootstrap", async (HttpContext context, EdgeTransport transport, TimeProvider time) =>
        {
            var user = await transport.GetAsync<IdentityUserResponse>("identity", "/internal/auth/me", context);
            if (!user.IsSuccess) return transport.ProblemFor(user, context);
            var value = user.Value!;
            return Results.Ok(new AppBootstrapResponse(
                new CurrentUserResponse(value.Id, value.Email, value.DisplayName),
                value.Timezone,
                value.JournalDayRollover,
                value.BaseCurrency,
                value.Appearance,
                value.Locale,
                value.AccentTheme,
                value.Role,
                value.AccountType,
                CockpitComposition.ResolveLocalDate(value.Timezone, time.GetUtcNow()),
                ProductAreas));
        });
    }
}
