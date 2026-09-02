namespace OrderHub.Application.Dtos;

/// <summary>Create payload for an order.</summary>
public sealed record CreateOrderRequest(string Name, string Description);

/// <summary>Update payload; `RowVersion` must round-trip the value the client last saw.</summary>
public sealed record UpdateOrderRequest(Guid RowVersion, string Name, string Description, IReadOnlyList<OrderBoardRequest> Boards);

/// <summary>Board assignment with per-order board quantity.</summary>
public sealed record OrderBoardRequest(Guid BoardId, int BoardQuantity);

/// <summary>Read model for an order (without navigation detail).</summary>
public sealed record OrderResponse(Guid Id, string Name, string Description, DateTime OrderDate, Guid RowVersion);

/// <summary>Detailed read model including board assignments.</summary>
public sealed record OrderDetailResponse(
    Guid Id,
    string Name,
    string Description,
    DateTime OrderDate,
    Guid RowVersion,
    IReadOnlyList<OrderBoardResponse> Boards);

/// <summary>Board assignment read model.</summary>
public sealed record OrderBoardResponse(Guid BoardId, string BoardName, int BoardQuantity);
