using System.ComponentModel.DataAnnotations;

namespace OrderHub.Domain;

/// <summary>
/// Join entity between <see cref="Board"/> and <see cref="Component"/>
/// with the extended attribute for the number of components placed per board.
/// </summary>
public class BoardComponent
{
    public Guid BoardId { get; set; }
    public Board Board { get; set; } = null!;

    public Guid ComponentId { get; set; }
    public Component Component { get; set; } = null!;

    /// <summary>Number of components placed per board.</summary>
    public int PlacementCount { get; set; }

    /// <summary>Optimistic concurrency token (PlacementCount can change independently of Board/Component).</summary>
    [ConcurrencyCheck]
    public Guid RowVersion { get; set; } = Guid.NewGuid();
}
