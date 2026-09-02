using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Shellwright.Api.Data;

/// <summary>Reads the reason out of a failed save.</summary>
public static class DbUpdateExceptions
{
    /// <summary>
    /// Whether a save failed because of a unique index.
    /// </summary>
    /// <param name="exception">The exception from <c>SaveChanges</c>.</param>
    /// <returns>True when the cause was a duplicate key.</returns>
    /// <remarks>
    /// ⚠️ The reason to ask this rather than re-query is that the re-query
    /// cannot answer. A row taken by another tenant is hidden from the caller
    /// by the tenant policy, so "does this slug exist" comes back false while
    /// the insert keeps failing — the endpoint would return 500 forever on a
    /// name it is quietly telling the user is available. The SQL state is the
    /// only source that knows what actually happened.
    /// </remarks>
    public static bool IsUniqueViolation(this DbUpdateException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception.InnerException is PostgresException postgres
            && postgres.SqlState == PostgresErrorCodes.UniqueViolation;
    }
}
