using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.Authorization;
using OrderHub.Client;
using OrderHub.Client.ApiClient;
using OrderHub.Client.Auth;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

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

await builder.Build().RunAsync();
