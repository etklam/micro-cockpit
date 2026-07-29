using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

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
                value.JournalDayRollover,
                value.BaseCurrency,
                value.Appearance,
                value.Locale,
                value.AccentTheme,
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
                value.JournalDayRollover,
                value.BaseCurrency,
                value.Appearance,
                value.Locale,
                value.AccentTheme,
                value.UpdatedAt));
        });

        app.MapGet("/api/app/account-export", async (
            HttpContext context, EdgeTransport transport, TimeProvider timeProvider) =>
        {
            var identityTask = transport.GetAsync<JsonElement>("identity", "/internal/auth/account-export", context);
            var journalTask = transport.GetAsync<JsonElement>("journal", "/internal/account-export", context);
            var toolTask = transport.GetAsync<JsonElement>("tool", "/internal/account-export", context);
            await Task.WhenAll(identityTask, journalTask, toolTask);
            var identity = await identityTask; var journal = await journalTask; var tools = await toolTask;
            if (!identity.IsSuccess) return transport.ProblemFor(identity, context);
            if (!journal.IsSuccess) return transport.ProblemFor(journal, context);
            if (!tools.IsSuccess) return transport.ProblemFor(tools, context);
            return Results.Ok(new AccountExportResponse(
                1, timeProvider.GetUtcNow().UtcDateTime, identity.Value, journal.Value, tools.Value));
        })
        .Produces<AccountExportResponse>(200);

        app.MapDelete("/api/app/account", async (
            [FromBody] AccountDeletionWrite body, HttpContext context, EdgeTransport transport) =>
        {
            if (body.Confirmation != "DELETE")
                return Results.Problem("confirmation_required", statusCode: 400);
            var journal = await transport.SendEmptyAsync("journal", "/internal/account-data", HttpMethod.Delete, context);
            if (!journal.IsSuccess) return transport.ProblemFor(journal, context);
            var tools = await transport.SendEmptyAsync("tool", "/internal/account-data", HttpMethod.Delete, context);
            if (!tools.IsSuccess) return transport.ProblemFor(tools, context);
            var identity = await transport.SendJsonEmptyAsync(
                "identity", "/internal/auth/account", HttpMethod.Delete, body, context);
            return identity.IsSuccess ? Results.NoContent() : transport.ProblemFor(identity, context);
        })
        .Produces(204).ProducesProblem(400);
    }
}
