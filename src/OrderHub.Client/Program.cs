using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.Authorization;
using OrderHub.Client;
using OrderHub.Client.ApiClient;
using OrderHub.Client.Auth;
using OrderHub.Client.RealTime;
using OrderHub.Client.Resources;
using Microsoft.Extensions.Localization;
using System.Globalization;
using Microsoft.JSInterop;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddLocalization();

// Typed API client: cookie credentials + CSRF header contract + API base address.
builder.Services.AddScoped(sp =>
{
    var http = new HttpClient(new BrowserCredentialsHandler())
    {
        BaseAddress = new Uri(builder.Configuration["ApiBaseUrl"] ?? builder.HostEnvironment.BaseAddress)
    };
    // CSRF contract: all mutating API requests must carry this header.
    http.DefaultRequestHeaders.Add("X-Requested-With", "OrderHub");
    return http;
});
builder.Services.AddScoped<IOrderHubApiClient, OrderHubApiClient>();

// Authentication state backed by the API cookie session.
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthenticationStateProvider, CookieAuthStateProvider>();

// Real-time concurrency notifications (one shared auto-reconnecting connection).
builder.Services.AddSingleton<IOrderHubClient, OrderHubClient>();

var host = builder.Build();

// Resolve the UI culture before anything renders: prefer the user's persisted
// choice in localStorage, otherwise the browser language when supported,
// otherwise the default.
var js = host.Services.GetRequiredService<IJSRuntime>();
var storedCulture = await js.InvokeAsync<string?>("localStorage.getItem", "appCulture");

var cultureName =
    !string.IsNullOrEmpty(storedCulture) && SupportedCultures.All.Contains(storedCulture)
        ? storedCulture
        : SupportedCultures.All.FirstOrDefault(c =>
            CultureInfo.CurrentUICulture.Name.Equals(c, StringComparison.OrdinalIgnoreCase)
            || CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals(
                c.Split('_')[0], StringComparison.OrdinalIgnoreCase))
        ?? SupportedCultures.Default;

var culture = new CultureInfo(cultureName);
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

await host.RunAsync();
