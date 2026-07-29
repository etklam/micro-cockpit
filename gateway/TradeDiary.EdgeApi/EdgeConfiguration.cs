using Microsoft.AspNetCore.Authorization;
using TradeDiary.Authorization;

internal static class EdgeServices
{
    internal static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>
    {
        ["identity"] = "http://127.0.0.1:5100",
        ["journal"] = "http://127.0.0.1:5101",
        ["market-data"] = "http://127.0.0.1:5106",
        ["tool"] = "http://127.0.0.1:5111",
    };
}

internal static class EdgeAuthorization
{
    internal static void Configure(AuthorizationOptions options)
    {
        TradeDiaryPolicies.Configure(options);
        options.AddPolicy("admin", policy => policy.RequireRole("admin"));
    }
}
