internal static class SettingsEndpoints
{
    internal static void Map(WebApplication app)
    {
        app.MapGet("/api/app/settings", async (HttpContext context, EdgeTransport transport) =>
        {
            var response = await transport.GetAsync<IdentitySettingsResponse>("identity", "/internal/auth/settings", context);
            if (!response.IsSuccess) return transport.ProblemFor(response, context);
            var value = response.Value!;
            return Results.Ok(new UserSettingsResponse(
                value.Email,
                value.DisplayName,
                value.Timezone,
                value.BaseCurrency,
                value.Appearance,
                value.UpdatedAt));
        });

        app.MapPut("/api/app/settings", async (UserSettingsWrite body, HttpContext context, EdgeTransport transport) =>
        {
            var response = await transport.SendJsonAsync<UserSettingsWrite, IdentitySettingsResponse>(
                "identity", "/internal/auth/settings", HttpMethod.Put, body, context);
            if (!response.IsSuccess) return transport.ProblemFor(response, context);
            var value = response.Value!;
            return Results.Ok(new UserSettingsResponse(
                value.Email,
                value.DisplayName,
                value.Timezone,
                value.BaseCurrency,
                value.Appearance,
                value.UpdatedAt));
        });
    }
}
