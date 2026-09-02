namespace OrderHub.Api.RealTime;

/// <summary>
/// Payload broadcast to watchers when an order is modified by another user.
/// </summary>
public sealed record OrderModifiedEvent(
    Guid OrderId,
    string Name,
    Guid NewRowVersion,
    string ModifiedBy,
    DateTimeOffset ModifiedAtUtc);
