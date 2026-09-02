namespace OrderHub.Client.RealTime;

/// <summary>
/// Payload of the OrderModifiedByAnotherUser event broadcast by the API
/// (mirrors OrderHub.Api.RealTime.OrderModifiedEvent).
/// </summary>
public sealed record OrderModifiedEvent(
    Guid OrderId,
    string Name,
    Guid NewRowVersion,
    string ModifiedBy,
    DateTimeOffset ModifiedAtUtc);
