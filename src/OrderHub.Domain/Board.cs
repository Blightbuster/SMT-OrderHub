using System.ComponentModel.DataAnnotations;

namespace OrderHub.Domain;

/// <summary>
/// A PCB board that can be produced across one or multiple orders.
/// </summary>
public class Board
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Board model reference (e.g. "MCU-Mainboard-v2").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Mechanical/electrical specifications.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Board length in millimeters.</summary>
    public double Length { get; set; }

    /// <summary>Board width in millimeters.</summary>
    public double Width { get; set; }

    /// <summary>Parent orders containing this board (many-to-many).</summary>
    public ICollection<OrderBoard> OrderBoards { get; set; } = new List<OrderBoard>();

    /// <summary>Components placed on this board (many-to-many).</summary>
    public ICollection<BoardComponent> BoardComponents { get; set; } = new List<BoardComponent>();

    /// <summary>Optimistic concurrency token.</summary>
    [ConcurrencyCheck]
    public Guid RowVersion { get; set; } = Guid.NewGuid();
}
