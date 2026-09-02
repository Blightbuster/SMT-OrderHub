using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace OrderHub.Api.Security;

/// <summary>
/// CSRF protection for cookie-based authentication from a cross-origin SPA.
///
/// The auth cookie is SameSite=None (required for the Blazor WebAssembly client
/// on a separate origin). CORS does NOT prevent cross-site *writes*: a malicious
/// page can POST/PUT/DELETE with credentials included and simply cannot read the
/// responses. State-changing verbs therefore require the custom header
/// "X-Requested-With: OrderHub", which cannot be attached by a cross-site form
/// or simple fetch without passing a CORS preflight — which attacker origins fail.
/// Safe methods (GET) are exempt: they must remain side-effect free.
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
