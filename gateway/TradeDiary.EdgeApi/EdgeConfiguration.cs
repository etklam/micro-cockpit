using Microsoft.AspNetCore.Authorization;

internal static class EdgeServices
{
    internal static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>
    {
        ["identity"] = "http://127.0.0.1:5100",
        ["journal"] = "http://127.0.0.1:5101",
        ["performance"] = "http://127.0.0.1:5102",
        ["discipline"] = "http://127.0.0.1:5103",
        ["reminder"] = "http://127.0.0.1:5104",
        ["stock-research"] = "http://127.0.0.1:5105",
        ["market-data"] = "http://127.0.0.1:5106",
        ["price-alert"] = "http://127.0.0.1:5107",
        ["rotation"] = "http://127.0.0.1:5108",
        ["partner"] = "http://127.0.0.1:5109",
        ["content"] = "http://127.0.0.1:5110",
        ["tool"] = "http://127.0.0.1:5111",
        ["operations"] = "http://127.0.0.1:5112"
    };
}

internal static class EdgeAuthorization
{
    internal static void Configure(AuthorizationOptions options)
    {
        var humanOnly = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireAssertion(context => context.User.FindFirst("account_type")?.Value != "agent")
            .Build();
        options.DefaultPolicy = humanOnly;
        options.FallbackPolicy = humanOnly;
        options.AddPolicy("diaryAccess", policy => policy.RequireAuthenticatedUser().RequireAssertion(context =>
        {
            if (context.User.FindFirst("account_type")?.Value != "agent") return true;
            var method = (context.Resource as HttpContext)?.Request.Method;
            var requiredScope = method == HttpMethods.Get ? "diary:read" : "diary:write";
            return context.User.FindAll("scope").Any(claim => claim.Value == requiredScope);
        }));
        options.AddPolicy("researchAccess", policy => policy.RequireAuthenticatedUser().RequireAssertion(context =>
        {
            if (context.User.FindFirst("account_type")?.Value != "agent") return true;
            var request = (context.Resource as HttpContext)?.Request;
            return request?.Method == HttpMethods.Get &&
                   context.User.FindAll("scope").Any(claim => claim.Value == "research:read");
        }));
        options.AddPolicy("admin", policy => policy.RequireRole("admin"));
    }
}
