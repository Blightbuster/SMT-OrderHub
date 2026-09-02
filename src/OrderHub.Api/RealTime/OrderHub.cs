using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace OrderHub.Api.RealTime;

/// <summary>
/// SignalR hub for real-time concurrency notifications.
/// Clients subscribe to an entity's group (e.g. WatchOrder(orderId)) while editing;
/// the backend broadcasts modification events so other editors can react (conflict banner).
/// </summary>
[Authorize]
public class OrderHub : Hub
{
    /// <summary>Subscribe to notifications for a specific order.</summary>
    public Task WatchOrder(Guid orderId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, GroupNameFor(orderId));

    /// <summary>Unsubscribe when leaving the edit view.</summary>
    public Task UnwatchOrder(Guid orderId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupNameFor(orderId));

    /// <summary>Canonical SignalR group name for an order channel.</summary>
    public static string GroupNameFor(Guid orderId) => $"order:{orderId}";
}
