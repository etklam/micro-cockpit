static class SourceReferenceValidator
{
    /// <summary>
    /// Verifies optional diary/transaction ownership through Journal using the caller's bearer
    /// token. Tool stores a soft historical reference and never reads Journal's schema directly.
    /// </summary>
    internal static async Task<bool> Owns(HttpClient client,HttpRequest request,Guid diaryId,Guid? transactionId)
    {
        var path=transactionId is null?$"/internal/diaries/{diaryId}":$"/internal/diaries/{diaryId}/transactions/{transactionId}";
        using var message=new HttpRequestMessage(HttpMethod.Get,path);var auth=request.Headers.Authorization.ToString();if(auth.Length>0)message.Headers.TryAddWithoutValidation("Authorization",auth);
        using var response=await client.SendAsync(message);return response.IsSuccessStatusCode;
    }
}
