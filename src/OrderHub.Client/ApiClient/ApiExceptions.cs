using System.Net;

namespace OrderHub.Client.ApiClient;

/// <summary>
/// Thrown when the API rejects a write because another user changed the record first.
/// Carries the current server-side state so the UI can offer a side-by-side review.
/// </summary>
public class ConcurrencyConflictException<TState>(TState currentState) : Exception(
    "The record was modified by another user while you were editing.")
{
    /// <summary>Latest database state (409 response body).</summary>
    public TState CurrentState { get; } = currentState;
}

/// <summary>Thrown when the API returns a validation error (400/422).</summary>
public class ApiValidationException(string message) : Exception(message);
