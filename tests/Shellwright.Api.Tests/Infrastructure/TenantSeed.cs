using Npgsql;
using Shellwright.Api.Domain;

namespace Shellwright.Api.Tests.Infrastructure;

/// <summary>One tenant's worth of rows, with the identifiers a test needs to assert on.</summary>
/// <param name="UserId">The member of the organisation.</param>
/// <param name="OrgId">The organisation.</param>
/// <param name="WorkspaceId">A workspace in it.</param>
/// <param name="AppId">An app in that workspace.</param>
/// <param name="AppName">The app's name, which is unique per seed so assertions can key on it.</param>
public sealed record SeededTenant(Guid UserId, Guid OrgId, Guid WorkspaceId, Guid AppId, string AppName);

/// <summary>Creates test tenants as the schema owner, which is exempt from every policy.</summary>
/// <remarks>
/// ⚠️ Seeding deliberately goes around row-level security. A test that had to
/// satisfy the policies in order to create its fixtures would only ever be able
/// to construct states the policies already allow — which is the opposite of
/// what these tests need to explore.
/// </remarks>
public static class TenantSeed
{
    /// <summary>Inserts a user, organisation, membership, workspace, and app.</summary>
    /// <param name="fixture">The database fixture.</param>
    /// <param name="label">A short label, mixed into names so failures are readable.</param>
    /// <param name="role">The membership role to grant.</param>
    /// <returns>The identifiers that were created.</returns>
    public static async Task<SeededTenant> CreateAsync(PostgresFixture fixture, string label, OrgRole role = OrgRole.Owner)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenant = new SeededTenant(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            $"{label}-app-{suffix}");

        var connection = await fixture.OpenAsOwnerAsync();
        await using (connection.ConfigureAwait(false))
        {
            var command = new NpgsqlCommand(
                """
                INSERT INTO users (id, email, created_at) VALUES (@user, @email, now());
                INSERT INTO orgs (id, name, slug, plan, created_at) VALUES (@org, @label, @slug, 'Free', now());
                INSERT INTO org_members (org_id, user_id, role, created_at) VALUES (@org, @user, @role, now());
                INSERT INTO workspaces (id, org_id, name, slug, created_at) VALUES (@workspace, @org, @label, 'default', now());
                INSERT INTO apps (id, workspace_id, name, bundle_id, created_at) VALUES (@app, @workspace, @appName, @bundleId, now());
                """,
                connection);

            await using (command.ConfigureAwait(false))
            {
                command.Parameters.AddWithValue("user", tenant.UserId);
                command.Parameters.AddWithValue("email", $"{label}-{suffix}@example.test");
                command.Parameters.AddWithValue("org", tenant.OrgId);
                command.Parameters.AddWithValue("label", $"{label} {suffix}");
                command.Parameters.AddWithValue("slug", $"{label}-{suffix}");
                command.Parameters.AddWithValue("role", role.ToString());
                command.Parameters.AddWithValue("workspace", tenant.WorkspaceId);
                command.Parameters.AddWithValue("app", tenant.AppId);
                command.Parameters.AddWithValue("appName", tenant.AppName);
                command.Parameters.AddWithValue("bundleId", $"test.{label}.s{suffix}");
                await command.ExecuteNonQueryAsync();
            }
        }

        return tenant;
    }

    /// <summary>Inserts a user with no organisation membership at all.</summary>
    /// <param name="fixture">The database fixture.</param>
    /// <returns>The new user's id.</returns>
    public static async Task<Guid> CreateUserAsync(PostgresFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var id = Guid.CreateVersion7();
        var connection = await fixture.OpenAsOwnerAsync();
        await using (connection.ConfigureAwait(false))
        {
            var command = new NpgsqlCommand(
                "INSERT INTO users (id, email, created_at) VALUES (@id, @email, now())",
                connection);

            await using (command.ConfigureAwait(false))
            {
                command.Parameters.AddWithValue("id", id);
                command.Parameters.AddWithValue("email", $"lone-{id:N}@example.test");
                await command.ExecuteNonQueryAsync();
            }
        }

        return id;
    }
}
