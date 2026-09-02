namespace OrderHub.Client.ApiClient;

/// <summary>Read model for a component (mirrors the API contract).</summary>
public sealed record ComponentDto(Guid Id, string Name, string Description, int Quantity, Guid RowVersion);

public sealed record PagedResultDto<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);

public sealed record CreateComponentRequest(string Name, string Description, int Quantity);
public sealed record UpdateComponentRequest(Guid RowVersion, string Name, string Description, int Quantity);

/// <summary>Read model for a board with its component placements.</summary>
public sealed record BoardDto(Guid Id, string Name, string Description, double Length, double Width, Guid RowVersion);
public sealed record BoardDetailDto(Guid Id, string Name, string Description, double Length, double Width, Guid RowVersion, IReadOnlyList<PlacementDto> Components);
public sealed record PlacementDto(Guid ComponentId, string ComponentName, int PlacementCount);
public sealed record CreateBoardRequest(string Name, string Description, double Length, double Width);
public sealed record UpdateBoardRequest(Guid RowVersion, string Name, string Description, double Length, double Width, IReadOnlyList<PlacementRequest> Components);
public sealed record PlacementRequest(Guid ComponentId, int PlacementCount);

/// <summary>Read model for an order with its board assignments.</summary>
public sealed record OrderDto(Guid Id, string Name, string Description, DateTime OrderDate, Guid RowVersion);
public sealed record OrderDetailDto(Guid Id, string Name, string Description, DateTime OrderDate, Guid RowVersion, IReadOnlyList<BoardAssignmentDto> Boards);
public sealed record BoardAssignmentDto(Guid BoardId, string BoardName, int BoardQuantity);
public sealed record CreateOrderRequest(string Name, string Description);
public sealed record UpdateOrderRequest(Guid RowVersion, string Name, string Description, IReadOnlyList<BoardAssignmentRequest> Boards);
public sealed record BoardAssignmentRequest(Guid BoardId, int BoardQuantity);
