using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using OrderHub.Client.ApiClient;

namespace OrderHub.Client.Auth;

/// <summary>
/// Authentication state provider for the Blazor WASM client backed by the
/// API's cookie session. Calls /manage/info (Identity endpoint) to determine
/// whether the user is signed in.
/// </summary>
public class CookieAuthStateProvider : AuthenticationStateProvider
{
    private static readonly ClaimsIdentity Anonymous = new();

    private readonly IOrderHubApiClient _api;
    private ClaimsIdentity _identity = Anonymous;

    public CookieAuthStateProvider(IOrderHubApiClient api) => _api = api;

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (_identity == Anonymous)
        {
            try
            {
                var info = await _api.Auth.GetUserInfoAsync();
                if (info is not null)
                {
                    var claims = new List<Claim>();
                    if (!string.IsNullOrEmpty(info.Email))
                    {
                        claims.Add(new Claim(ClaimTypes.Name, info.Email));
                        claims.Add(new Claim(ClaimTypes.NameIdentifier, info.Email));
                    }
                    _identity = new ClaimsIdentity(claims, authenticationType: "cookie");
                }
            }
            catch
            {
                _identity = Anonymous; // network/API failure → treat as signed out
            }
        }

        return new AuthenticationState(new ClaimsPrincipal(_identity));
    }

    /// <summary>Re-evaluates the auth state (after login/logout).</summary>
    public void NotifyStateChanged()
    {
        _identity = Anonymous;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
}
