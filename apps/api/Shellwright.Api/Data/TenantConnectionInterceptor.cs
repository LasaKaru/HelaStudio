using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Shellwright.Api.Data;

/// <summary>
/// Stamps <c>app.user_id</c> onto every database connection the moment it opens,
/// so that row-level security has an identity to evaluate against.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ The setting is applied on <em>every</em> open, including when there is no
/// user. Npgsql pools physical connections, and a session-level setting
/// survives being returned to the pool: skipping the write when the identity is
/// null would leave the previous request's identity in place for the next
/// borrower of that connection. Writing an empty string unconditionally is what
/// makes pooling safe here, and it is why the "no identity" case is a write
/// rather than a no-op.
/// </para>
/// <para>
/// It is set at session scope rather than with <c>SET LOCAL</c> because EF Core
/// runs plenty of reads outside an explicit transaction, and <c>SET LOCAL</c>
/// outside a transaction is silently discarded — the most dangerous shape a
/// security control can take.
/// </para>
/// </remarks>
public sealed class TenantConnectionInterceptor(TenantContext tenant) : DbConnectionInterceptor
{
    /// <summary>The GUC the policies read.</summary>
    public const string SettingName = "app.user_id";

    /// <inheritdoc />
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(connection);
        Apply(connection);
    }

    /// <inheritdoc />
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await ApplyAsync(connection, cancellationToken);
    }

    private void Apply(DbConnection connection)
    {
        using var command = CreateCommand(connection);
        command.ExecuteNonQuery();
    }

    private async Task ApplyAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        var command = CreateCommand(connection);
        await using (command.ConfigureAwait(false))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private DbCommand CreateCommand(DbConnection connection)
    {
        var command = connection.CreateCommand();

        // Parameterised, not interpolated. The value is a GUID today, but a
        // set_config call built by string concatenation is a SQL injection
        // sink that only ever gets noticed after it has been exploited.
        command.CommandText = "SELECT set_config(@name, @value, false)";

        var name = command.CreateParameter();
        name.ParameterName = "name";
        name.Value = SettingName;
        command.Parameters.Add(name);

        var value = command.CreateParameter();
        value.ParameterName = "value";
        value.Value = tenant.UserId?.ToString() ?? string.Empty;
        command.Parameters.Add(value);

        return command;
    }

    /// <summary>Applies the current identity to a connection this interceptor does not own.</summary>
    /// <param name="connection">An open Npgsql connection.</param>
    /// <param name="userId">The identity to stamp, or null for none.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the setting is applied.</returns>
    /// <remarks>
    /// Used by tests that talk raw SQL. A test that reached the database
    /// through a different code path from production would be testing the
    /// wrong thing, so the statement lives here and both callers use it.
    /// </remarks>
    public static async Task ApplyAsync(NpgsqlConnection connection, Guid? userId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var command = new NpgsqlCommand("SELECT set_config(@name, @value, false)", connection);
        await using (command.ConfigureAwait(false))
        {
            command.Parameters.AddWithValue("name", SettingName);
            command.Parameters.AddWithValue("value", userId?.ToString() ?? string.Empty);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
