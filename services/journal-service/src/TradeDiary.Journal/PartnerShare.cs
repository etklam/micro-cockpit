using System.Net.Http.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Journal asks Partner for share authorization. Journal never reads the partner schema.
/// </summary>
static class PartnerShare
{
    internal static async Task<bool?> IsDiarySharedAsync(IHttpClientFactory httpFactory, HttpRequest request, Guid ownerId)
    {
        var client = httpFactory.CreateClient("partner");
        using var message = new HttpRequestMessage(
            HttpMethod.Get,
            $"/internal/partners/{ownerId:D}/authorization?resource=diary");
        var auth = request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(auth))
            message.Headers.TryAddWithoutValidation("Authorization", auth);

        try
        {
            using var response = await client.SendAsync(message);
            if (!response.IsSuccessStatusCode) return null;
            var body = await response.Content.ReadFromJsonAsync<AuthorizationBody>();
            return body?.Allowed == true;
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
    }

    private sealed record AuthorizationBody([property: JsonPropertyName("allowed")] bool Allowed);
}
