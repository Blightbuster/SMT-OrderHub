using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace OrderHub.Client.Auth;

/// <summary>
/// Applies browser fetch credentials ("include") to every outgoing request so the
/// API session cookie travels with each cross-origin call in Blazor WASM.
///
/// This is the WASM equivalent of the built-in cookie behavior on other hosts:
/// in WebAssembly, HttpClient delegates to browser fetch(), which defaults to
/// credentials "same-origin" — cookies for the API's separate origin would never
/// be attached. There is no client-level flag for this; the per-request setting
/// must be applied here. The official Microsoft.AspNetCore.Components.WebAssembly.
/// Authentication library registers an equivalent DelegatingHandler internally.
/// </summary>
internal sealed class BrowserCredentialsHandler : DelegatingHandler
{
    public BrowserCredentialsHandler() : base(new HttpClientHandler()) { }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        return base.SendAsync(request, cancellationToken);
    }
}
