using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace OrderHub.Api.Security;

/// <summary>
/// CSRF defense-in-depth for cookie-based authentication.
///
/// In production, the client (orderhub.scheve.org) and API (api.orderhub.scheve.org)
/// are same-site (sharing the scheve.org registrable domain) but cross-origin.
/// Because they are same-site, SameSite=Lax allows the Blazor SPA to send the auth
/// cookie on fetch/XHR with credentials.
///
/// Although CORS restricts cross-origin response reading, simple cross-site requests
/// from other contexts could attempt mutating operations. Requiring the custom header
/// "X-Requested-With: OrderHub" triggers a CORS preflight that unauthorized origins
/// fail. Safe methods (GET, HEAD, OPTIONS) are exempt.
/// </summary>
public class RequireCsrfHeaderAttribute : Attribute, IAuthorizationFilter
{
    public const string HeaderName = "X-Requested-With";
    public const string ExpectedValue = "OrderHub";

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        // Only guard state-changing verbs.
        var method = context.HttpContext.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
        {
            return;
        }

        // Same-origin browser requests (e.g. Swagger "try it out") are already
        // protected by SameSite cookie rules; the header is mandatory for all
        // clients regardless, keeping the contract uniform and simple.
        if (!context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var value) ||
            !string.Equals(value, ExpectedValue, StringComparison.Ordinal))
        {
            context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
        }
    }
}
