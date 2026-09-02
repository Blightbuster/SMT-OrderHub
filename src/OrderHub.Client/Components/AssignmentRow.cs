namespace OrderHub.Client.Components;

/// <summary>
/// Editable row model for an assignment list (board→component placements,
/// order→board assignments). Non-generic by design: the row only carries the
/// referenced item's id + display label + count, which is identical for every
/// assignment type. One shared AssignmentList component can therefore render it.
/// </summary>
public class AssignmentRow
{
    /// <summary>Id of the referenced item (component id / board id).</summary>
    public required Guid ItemId { get; set; }

    /// <summary>Display name of the referenced item.</summary>
    public required string Label { get; set; }

    /// <summary>PlacementCount / BoardQuantity.</summary>
    public int Count { get; set; } = 1;
}
