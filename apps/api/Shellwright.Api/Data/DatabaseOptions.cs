using System.ComponentModel.DataAnnotations;

namespace Shellwright.Api.Data;

/// <summary>Database connection settings.</summary>
public sealed class DatabaseOptions
{
    /// <summary>Configuration section these settings bind to.</summary>
    public const string SectionName = "Database";

    /// <summary>
    /// The connection the API uses for all request handling.
    /// </summary>
    /// <remarks>
    /// ⚠️ Must name a role that neither owns the tables nor holds
    /// <c>BYPASSRLS</c>. <c>RowLevelSecurityTests.Application_role_is_not_privileged</c>
    /// asserts this against the live database, because a deployment that gets
    /// it wrong looks completely healthy right up until it serves one tenant's
    /// data to another.
    /// </remarks>
    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// The connection migrations run as, owning the schema and every DDL change.
    /// </summary>
    /// <remarks>
    /// Empty in environments that do not migrate, such as a read replica or a
    /// container that has migrations applied to it by a separate job.
    /// </remarks>
    public string MigrationConnectionString { get; set; } = string.Empty;
}
