using System.ComponentModel.DataAnnotations;

namespace OrderHub.Domain;

/// <summary>
/// Join entity between <see cref="Order"/> and <see cref="Board"/>
/// with the extended attribute for the quantity of boards per order.
/// </summary>
public class OrderBoard
{
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public Guid BoardId { get; set; }
    public Board Board { get; set; } = null!;

    /// <summary>Quantity of boards produced per order.</summary>
    public int BoardQuantity { get; set; }

    /// <summary>Optimistic concurrency token (BoardQuantity can change independently of Order/Board).</summary>
    [ConcurrencyCheck]
    public Guid RowVersion { get; set; } = Guid.NewGuid();
}
