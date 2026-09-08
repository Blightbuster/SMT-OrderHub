using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Serilog;

namespace OrderHub.Api.RealTime;

/// <summary>
/// SignalR hub for real-time concurrency notifications.
/// Clients subscribe to an entity's group (e.g. WatchOrder(orderId)) while editing;
/// the backend broadcasts modification events so other editors can react (conflict banner).
/// </summary>
[Authorize]
public class EntityHub : Hub
{
    private static readonly Serilog.ILogger HubLog = Log.ForContext<EntityHub>();

    /// <summary>
    /// Payload broadcast to watchers when any entity (order, board or component)
    /// is modified by another user.
    /// </summary>
    public sealed record EntityModifiedEvent(
        Guid Id,
        Guid NewRowVersion,
        string ModifiedBy,
        DateTimeOffset ModifiedAtUtc);

    /// <summary>Subscribe to notifications for a specific order.</summary>
    public async Task WatchOrder(Guid orderId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupNameForOrder(orderId));
        HubLog.Information("User {User} ({ConnectionId}) started watching order {OrderId}",
            Context.User?.Identity?.Name ?? "unknown", Context.ConnectionId, orderId);
    }

    /// <summary>Unsubscribe when leaving the edit view.</summary>
    public Task UnwatchOrder(Guid orderId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupNameForOrder(orderId));

    /// <summary>Canonical SignalR group name for an order channel.</summary>
    public static string GroupNameForOrder(Guid orderId) => $"order:{orderId}";

    /// <summary>Subscribe to notifications for a specific board.</summary>
    public async Task WatchBoard(Guid boardId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupNameForBoard(boardId));
        HubLog.Information("User {User} ({ConnectionId}) started watching board {BoardId}",
            Context.User?.Identity?.Name ?? "unknown", Context.ConnectionId, boardId);
    }

    /// <summary>Unsubscribe when leaving the edit view.</summary>
    public Task UnwatchBoard(Guid boardId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupNameForBoard(boardId));

    /// <summary>Canonical SignalR group name for a board channel.</summary>
    public static string GroupNameForBoard(Guid boardId) => $"board:{boardId}";

    /// <summary>Subscribe to notifications for a specific component.</summary>
    public async Task WatchComponent(Guid componentId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupNameForComponent(componentId));
        HubLog.Information("User {User} ({ConnectionId}) started watching component {ComponentId}",
            Context.User?.Identity?.Name ?? "unknown", Context.ConnectionId, componentId);
    }

    /// <summary>Unsubscribe when leaving the edit view.</summary>
    public Task UnwatchComponent(Guid componentId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupNameForComponent(componentId));

    /// <summary>Canonical SignalR group name for a component channel.</summary>
    public static string GroupNameForComponent(Guid componentId) => $"component:{componentId}";
}
