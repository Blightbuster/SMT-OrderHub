using Microsoft.AspNetCore.Components.WebAssembly.Http;
using Microsoft.AspNetCore.SignalR.Client;

namespace OrderHub.Client.RealTime;

/// <summary>
/// Shared SignalR connection for real-time concurrency notifications.
/// One auto-reconnecting connection per app; components subscribe per-order
/// handlers instead of managing their own connections.
/// </summary>
public interface IOrderHubClient
{
    HubConnection Connection { get; }

    /// <summary>Starts the connection (idempotent).</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Join the watch group for a specific order.</summary>
    Task WatchOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>Leave the watch group (called when leaving the edit view).</summary>
    Task UnwatchOrderAsync(Guid orderId, CancellationToken cancellationToken = default);
}

public class OrderHubClient : IOrderHubClient
{
    private readonly HubConnection _connection;

    public OrderHubClient(IConfiguration configuration)
    {
        var apiBaseUrl = configuration["ApiBaseUrl"]
            ?? throw new InvalidOperationException("ApiBaseUrl is not configured.");

        _connection = new HubConnectionBuilder()
            .WithUrl($"{apiBaseUrl.TrimEnd('/')}/hubs/orders", options =>
            {
                // Send the auth cookie with negotiate + transport requests.
                options.HttpMessageHandlerFactory = inner => new CookieCredentialsHandler(inner);
            })
            .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30) })
            .Build();
    }

    public HubConnection Connection => _connection;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_connection.State == HubConnectionState.Disconnected)
        {
            await _connection.StartAsync(cancellationToken);
        }
    }

    public Task WatchOrderAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        _connection.InvokeAsync("WatchOrder", orderId, cancellationToken);

    public Task UnwatchOrderAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        _connection.InvokeAsync("UnwatchOrder", orderId, cancellationToken);

    public void Dispose() => _ = _connection.DisposeAsync().AsTask();
}

/// <summary>
/// Sets browser fetch credentials ("include") on every hub request so the
/// API session cookie travels with negotiate + LongPolling transports.
/// </summary>
internal sealed class CookieCredentialsHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        return base.SendAsync(request, cancellationToken);
    }
}
