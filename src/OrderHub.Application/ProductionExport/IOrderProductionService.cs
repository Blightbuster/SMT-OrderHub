using OrderHub.Application.Interfaces;

namespace OrderHub.Application.ProductionExport;

/// <summary>
/// Exports an order aggregate (Order → Boards → Components) as a
/// production-line-ready JSON payload.
/// </summary>
public interface IOrderProductionService
{
    /// <summary>
    /// Aggregates the order with all board/component assignments and serializes
    /// it to a structured JSON payload for the SMT production line.
    /// </summary>
    /// <exception cref="EntityNotFoundException">Order does not exist.</exception>
    /// <exception cref="InvalidOperationException">Order has no boards assigned.</exception>
    Task<string> ExportOrderForProductionAsync(Guid orderId, CancellationToken cancellationToken = default);
}
