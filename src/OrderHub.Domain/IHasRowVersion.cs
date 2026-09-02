namespace OrderHub.Domain;

/// <summary>
/// Contract for entities carrying an optimistic concurrency token.
/// Enables the DbContext to centrally regenerate tokens on save.
/// </summary>
public interface IHasRowVersion
{
    Guid RowVersion { get; set; }
}
