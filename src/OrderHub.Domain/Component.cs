using System.ComponentModel.DataAnnotations;

namespace OrderHub.Domain;

/// <summary>
/// An SMT component that can be placed on one or multiple boards.
/// </summary>
public class Component : IHasRowVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Part identifier (e.g. "Resistor 10k 0805", "STM32F407").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Package specifications or manufacturer SKU.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Stock / available reel count.</summary>
    public int Quantity { get; set; }

    /// <summary>Boards this component is placed on (many-to-many).</summary>
    public ICollection<BoardComponent> BoardComponents { get; set; } = new List<BoardComponent>();

    /// <summary>Optimistic concurrency token.</summary>
    [ConcurrencyCheck]
    public Guid RowVersion { get; set; } = Guid.NewGuid();
}
