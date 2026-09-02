namespace OrderHub.Application.Dtos;

/// <summary>
/// Shared paged-result envelope for list endpoints.
/// </summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
