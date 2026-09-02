namespace OrderHub.Application.Dtos;

/// <summary>Create payload for a board.</summary>
public sealed record CreateBoardRequest(string Name, string Description, double Length, double Width);

/// <summary>Update payload; `RowVersion` must round-trip the value the client last saw.</summary>
public sealed record UpdateBoardRequest(Guid RowVersion, string Name, string Description, double Length, double Width, IReadOnlyList<BoardComponentRequest> Components);

/// <summary>Component placement with per-board placement count.</summary>
public sealed record BoardComponentRequest(Guid ComponentId, int PlacementCount);

/// <summary>Read model for a board (without navigation detail).</summary>
public sealed record BoardResponse(Guid Id, string Name, string Description, double Length, double Width, Guid RowVersion);

/// <summary>Detailed read model including component placements.</summary>
public sealed record BoardDetailResponse(
    Guid Id,
    string Name,
    string Description,
    double Length,
    double Width,
    Guid RowVersion,
    IReadOnlyList<BoardComponentResponse> Components);

/// <summary>Component placement read model.</summary>
public sealed record BoardComponentResponse(Guid ComponentId, string ComponentName, int PlacementCount);
