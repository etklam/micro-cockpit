using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace TradeDiary.Authorization;

public static class TradeDiaryPolicies
{
    public const string JournalAccess = "journalAccess";
    public const string AgentRead = "agentRead";

    public static void Configure(AuthorizationOptions options)
    {
        var humanOnly = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireAssertion(context => !IsAgent(context))
            .Build();
        options.DefaultPolicy = humanOnly;
        options.FallbackPolicy = humanOnly;
        options.AddPolicy(JournalAccess, policy => policy
            .RequireAuthenticatedUser()
            .RequireAssertion(context =>
            {
                if (!IsAgent(context)) return true;
                var method = (context.Resource as HttpContext)?.Request.Method;
                var required = method == HttpMethods.Get ? "journal:read" : "journal:write";
                return HasScope(context, required);
            }));
        options.AddPolicy(AgentRead, policy => policy
            .RequireAuthenticatedUser()
            .RequireAssertion(context =>
                !IsAgent(context)
                || ((context.Resource as HttpContext)?.Request.Method == HttpMethods.Get
                    && HasScope(context, "agent:read"))));
    }

    public static bool IsAgent(AuthorizationHandlerContext context) =>
        context.User.FindFirst("account_type")?.Value == "agent";

    public static bool HasScope(AuthorizationHandlerContext context, string scope) =>
        context.User.FindAll("scope").Any(claim => claim.Value == scope);
}
