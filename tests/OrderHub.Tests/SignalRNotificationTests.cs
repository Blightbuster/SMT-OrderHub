using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrderHub.Infrastructure.Persistence;

namespace OrderHub.Tests;

/// <summary>
/// End-to-end SignalR test: two users watch the same order; user A modifies it
/// via the REST API; user B receives the real-time OrderModifiedByAnotherUser event.
/// </summary>
public class SignalRNotificationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WebApplicationFactory<Program> _factory;

    public SignalRNotificationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAllDbContexts();
                    services.AddDbContext<SmtDbContext>(options => options.UseSqlite(_connection));

                    using var scope = services.BuildServiceProvider().CreateScope();
                    scope.ServiceProvider.GetRequiredService<SmtDbContext>().Database.EnsureCreated();
                });
            });
    }

    [Fact]
    public async Task ModifyOrder_NotifiesOtherWatchers()
    {
        // ----- Arrange: two authenticated users -----
        var userA = await CreateUserAsync();
        var userB = await CreateUserAsync();

        // User A creates an order and shares its id with user B.
        var created = await (await userA.Client.PostAsJsonAsync("/api/orders",
            new { name = "SMT-RUN-SIGNALR", description = "watch me" })).Content.ReadFromJsonAsync<JsonElement>();
        var orderId = created.GetProperty("id").GetGuid();
        var originalRowVersion = created.GetProperty("rowVersion").GetGuid();

        // Both connections subscribe to the order channel.
        var received = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        userB.Connection.On<JsonElement>("OrderModifiedByAnotherUser", e => received.TrySetResult(e));

        await userA.Connection.StartAsync();
        await userB.Connection.StartAsync();
        await userA.Connection.InvokeAsync("WatchOrder", orderId);
        await userB.Connection.InvokeAsync("WatchOrder", orderId);

        // ----- Act: user A modifies the order via REST -----
        var put = await userA.Client.PutAsJsonAsync($"/api/orders/{orderId}", new
        {
            rowVersion = originalRowVersion,
            name = "SMT-RUN-SIGNALR",
            description = "modified by A",
            boards = Array.Empty<object>()
        });
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        // ----- Assert: user B receives the broadcast -----
        var receivedTask = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.True(receivedTask == received.Task, "No OrderModifiedByAnotherUser event received within timeout.");

        var evt = received.Task.Result;
        Assert.Equal(orderId, evt.GetProperty("orderId").GetGuid());
        // The broadcast must carry the NEW RowVersion (bumped on save), not the original.
        Assert.NotEqual(originalRowVersion, evt.GetProperty("newRowVersion").GetGuid());
        Assert.False(string.IsNullOrWhiteSpace(evt.GetProperty("modifiedBy").GetString()));

        await userA.Connection.DisposeAsync();
        await userB.Connection.DisposeAsync();
        userA.Client.Dispose();
        userB.Client.Dispose();
    }

    private async Task<(HttpClient Client, HubConnection Connection)> CreateUserAsync()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Requested-With", "OrderHub");

        var userName = $"signalr-{Guid.NewGuid():N}@orderhub.test";
        var password = "Smt!Passw0rd-42";
        (await client.PostAsJsonAsync("/register", new { email = userName, password })).EnsureSuccessStatusCode();

        // Capture the auth cookie straight from the login response.
        var loginResponse = await client.PostAsJsonAsync("/login?useCookies=true", new { email = userName, password });
        loginResponse.EnsureSuccessStatusCode();
        var setCookie = loginResponse.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith("OrderHub.Auth="));
        var authCookie = setCookie.Split(';')[0]; // "OrderHub.Auth=<value>"

        // Hub connection over the test server's in-memory pipeline.
        // LongPolling transport: WebSockets bypass HttpMessageHandler and would try
        // a real TCP connect to localhost:80, which does not exist in the test host.
        var connection = new HubConnectionBuilder()
            .WithUrl(_factory.Server.BaseAddress + "hubs/orders", options =>
            {
                options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
                // Route ALL hub HTTP traffic through the test server's in-memory
                // handler, wrapped to forward the authentication cookie.
                options.HttpMessageHandlerFactory = inner =>
                    new CookieForwardingHandler(authCookie, _factory.Server.CreateHandler());
            })
            .Build();

        return (client, connection);
    }

    /// <summary>Routes hub traffic through the test server while forwarding the auth cookie.</summary>
    private sealed class CookieForwardingHandler(string authCookie, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            request.Headers.TryAddWithoutValidation("Cookie", authCookie);
            return base.SendAsync(request, cancellationToken);
        }
    }

    public void Dispose()
    {
        _factory.Dispose();
        _connection.Dispose();
    }
}
