using Microsoft.EntityFrameworkCore;

namespace OrderHub.Api.Controllers;

/// <summary>
/// Helpers for translating low-level database failures into meaningful API errors.
/// </summary>
public static class DbUpdateExceptionExtensions
{
    /// <summary>
    /// Detects a unique index/constraint violation (e.g. duplicate entity name).
    /// SQLite reports this as error code 19 (constraint) with a UNIQUE message.
    /// </summary>
    public static bool IsUniqueConstraintViolation(this DbUpdateException ex) =>
        ex.InnerException is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19 } sqlite
        && sqlite.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase);
}
