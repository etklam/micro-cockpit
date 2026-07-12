using System.Security.Cryptography;
using System.Text.Json;

public static class IdempotencyRules
{
    public static string? Normalize(string? value)
    {
        var key = value?.Trim();
        return string.IsNullOrEmpty(key) ? null : key;
    }

    public static bool IsValid(string? key) => key is null || key.Length <= 200;

    public static string ComputeRequestHash<T>(T payload) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload)));
}
