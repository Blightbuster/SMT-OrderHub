using System.ComponentModel.DataAnnotations;

namespace OrderHub.Domain;

/// <summary>
/// A production order in the SMT manufacturing flow.
/// </summary>
public class Order : IHasRowVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Human-readable order title (e.g. "SMT-RUN-2026-001").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Detailed notes or batch parameters.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Timestamp of creation (UTC).</summary>
    public DateTime OrderDate { get; set; } = DateTime.UtcNow;

    /// <summary>Boards associated with this order (many-to-many).</summary>
    public ICollection<OrderBoard> OrderBoards { get; set; } = new List<OrderBoard>();

    /// <summary>Optimistic concurrency token.</summary>
    [ConcurrencyCheck]
    public Guid RowVersion { get; set; } = Guid.NewGuid();
}
