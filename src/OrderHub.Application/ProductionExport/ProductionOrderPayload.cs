namespace OrderHub.Application.ProductionExport;

/// <summary>
/// Production-line-ready payload for a single board of an order.
/// </summary>
public sealed record ProductionBoard(
    string Name,
    double LengthMm,
    double WidthMm,
    int Quantity,
    IReadOnlyList<ProductionPlacement> Placements);

/// <summary>Placement of a component on a production board.</summary>
public sealed record ProductionPlacement(
    string Name,
    string Description,
    int PlacementCount);

/// <summary>
/// Root payload handed to an SMT production line controller for a given order.
/// </summary>
public sealed record ProductionOrderPayload(
    Guid OrderId,
    string Name,
    string Description,
    DateTime OrderDateUtc,
    IReadOnlyList<ProductionBoard> Boards);
