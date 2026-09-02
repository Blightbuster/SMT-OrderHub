namespace OrderHub.Application.Dtos;

/// <summary>Create payload for a component.</summary>
public sealed record CreateComponentRequest(string Name, string Description, int Quantity);

/// <summary>Update payload; `RowVersion` must round-trip the value the client last saw.</summary>
public sealed record UpdateComponentRequest(Guid RowVersion, string Name, string Description, int Quantity);

/// <summary>Read model for a component.</summary>
public sealed record ComponentResponse(Guid Id, string Name, string Description, int Quantity, Guid RowVersion);
