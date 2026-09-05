using System.Diagnostics.CodeAnalysis;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shellwright.Api.Domain;
using Shellwright.Api.Tests.Infrastructure;
using Xunit;

namespace Shellwright.Api.Tests;

/// <summary>
/// The tenant isolation guarantee, asserted at the SQL level rather than
/// through the API.
/// </summary>
/// <remarks>
/// ⚠️ These tests go around the application on purpose. Asserting isolation
/// through an endpoint proves the endpoint's <c>WHERE</c> clause is right
/// today; asserting it with raw SQL as the role the API actually connects as
/// proves that the next endpoint, written by somebody in a hurry, cannot get
/// it wrong.
/// </remarks>
[Collection(DatabaseFixtureDefinition.Name)]
public sealed class RowLevelSecurityTests(PostgresFixture fixture)
{
    /// <summary>TC-S06-SEC-001 — raw SQL as one tenant returns only that tenant's rows.</summary>
    [Fact]
    public async Task Raw_select_as_one_tenant_never_returns_another_tenants_apps()
    {
        var alpha = await TenantSeed.CreateAsync(fixture, "alpha");
        var beta = await TenantSeed.CreateAsync(fixture, "beta");

        var visible = await AppNamesAsync(alpha.UserId);

        visible.Should().Contain(alpha.AppName);
        visible.Should().NotContain(beta.AppName);
    }

    /// <summary>A connection with no identity stamped sees nothing, rather than everything.</summary>
    [Fact]
    public async Task Unset_identity_sees_nothing()
    {
        await TenantSeed.CreateAsync(fixture, "unset");

        var visible = await AppNamesAsync(userId: null);

        visible.Should().BeEmpty();
    }

    /// <summary>A signed-in user with no memberships sees nothing.</summary>
    [Fact]
    public async Task User_with_no_memberships_sees_nothing()
    {
        await TenantSeed.CreateAsync(fixture, "someone");
        var stranger = await TenantSeed.CreateUserAsync(fixture);

        var visible = await AppNamesAsync(stranger);

        visible.Should().BeEmpty();
    }

    /// <summary>Isolation holds through EF Core too, not only through hand-written SQL.</summary>
    [Fact]
    public async Task Ef_queries_are_scoped_by_the_connection_interceptor()
    {
        var alpha = await TenantSeed.CreateAsync(fixture, "efalpha");
        var beta = await TenantSeed.CreateAsync(fixture, "efbeta");

        var context = fixture.CreateContext(alpha.UserId);
        await using (context.ConfigureAwait(false))
        {
            var names = await context.Apps.Select(x => x.Name).ToListAsync();

            names.Should().Contain(alpha.AppName);
            names.Should().NotContain(beta.AppName);
        }
    }

    /// <summary>
    /// TC-S06-SEC-002's database half: another tenant's app is not merely
    /// forbidden, it does not exist.
    /// </summary>
    [Fact]
    public async Task Another_tenants_app_is_invisible_by_id()
    {
        var alpha = await TenantSeed.CreateAsync(fixture, "byid-a");
        var beta = await TenantSeed.CreateAsync(fixture, "byid-b");

        var context = fixture.CreateContext(alpha.UserId);
        await using (context.ConfigureAwait(false))
        {
            var found = await context.Apps.FirstOrDefaultAsync(x => x.Id == beta.AppId);

            found.Should().BeNull();
        }
    }

    /// <summary>Writing into another tenant's workspace is refused by the policy, not by the handler.</summary>
    [Fact]
    public async Task Inserting_into_another_tenants_workspace_is_refused()
    {
        var alpha = await TenantSeed.CreateAsync(fixture, "insert-a");
        var beta = await TenantSeed.CreateAsync(fixture, "insert-b");

        var context = fixture.CreateContext(alpha.UserId);
        await using (context.ConfigureAwait(false))
        {
            context.Apps.Add(new AppRecord
            {
                WorkspaceId = beta.WorkspaceId,
                Name = "smuggled",
                BundleId = "test.smuggled.app",
            });

            var save = async () => await context.SaveChangesAsync();

            var thrown = await save.Should().ThrowAsync<DbUpdateException>();
            thrown.Which.InnerException.Should().BeOfType<PostgresException>()
                .Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        }
    }

    /// <summary>Moving your own app into another tenant's workspace is refused by the same check.</summary>
    [Fact]
    public async Task Moving_an_app_into_another_tenants_workspace_is_refused()
    {
        var alpha = await TenantSeed.CreateAsync(fixture, "move-a");
        var beta = await TenantSeed.CreateAsync(fixture, "move-b");

        var context = fixture.CreateContext(alpha.UserId);
        await using (context.ConfigureAwait(false))
        {
            var app = await context.Apps.AsTracking().SingleAsync(x => x.Id == alpha.AppId);
            app.WorkspaceId = beta.WorkspaceId;

            var save = async () => await context.SaveChangesAsync();

            await save.Should().ThrowAsync<DbUpdateException>();
        }
    }

    /// <summary>The bootstrap window: you may claim an organisation nobody belongs to yet.</summary>
    [Fact]
    public async Task Creating_an_organisation_and_claiming_it_in_one_transaction_succeeds()
    {
        var user = await TenantSeed.CreateUserAsync(fixture);
        var orgId = Guid.CreateVersion7();

        var context = fixture.CreateContext(user);
        await using (context.ConfigureAwait(false))
        {
            var transaction = await context.Database.BeginTransactionAsync();
            await using (transaction.ConfigureAwait(false))
            {
                context.Orgs.Add(new Org { Id = orgId, Name = "Claimed", Slug = $"claimed-{orgId:N}"[..24] });
                context.OrgMembers.Add(new OrgMember { OrgId = orgId, UserId = user, Role = OrgRole.Owner });
                await context.SaveChangesAsync();
                await transaction.CommitAsync();
            }

            var mine = await context.Orgs.Select(x => x.Id).ToListAsync();
            mine.Should().Contain(orgId);
        }
    }

    /// <summary>...and the window closes: an organisation with a member cannot be claimed by an outsider.</summary>
    [Fact]
    public async Task Claiming_an_organisation_that_already_has_members_is_refused()
    {
        var existing = await TenantSeed.CreateAsync(fixture, "claimed");
        var intruder = await TenantSeed.CreateUserAsync(fixture);

        var context = fixture.CreateContext(intruder);
        await using (context.ConfigureAwait(false))
        {
            context.OrgMembers.Add(new OrgMember { OrgId = existing.OrgId, UserId = intruder, Role = OrgRole.Owner });

            var save = async () => await context.SaveChangesAsync();

            await save.Should().ThrowAsync<DbUpdateException>();
        }
    }

    /// <summary>
    /// The three preconditions without which every policy above is decoration.
    /// </summary>
    /// <remarks>
    /// ⚠️ This is the test that catches a misconfigured deployment. A stack that
    /// runs the API as the table owner passes every functional test in the
    /// suite and has no isolation at all, because a table's owner is exempt
    /// from its own policies.
    /// </remarks>
    [Fact]
    public async Task Application_role_owns_nothing_and_cannot_bypass_policies()
    {
        var connection = await fixture.OpenAsAppAsync(userId: null);
        await using (connection.ConfigureAwait(false))
        {
            var identity = await ScalarAsync<string>(connection, "SELECT current_user");
            identity.Should().Be("shellwright_app");

            var superuser = await ScalarAsync<bool>(
                connection,
                "SELECT rolsuper FROM pg_roles WHERE rolname = current_user");
            superuser.Should().BeFalse("a superuser is exempt from every policy");

            var bypass = await ScalarAsync<bool>(
                connection,
                "SELECT rolbypassrls FROM pg_roles WHERE rolname = current_user");
            bypass.Should().BeFalse("BYPASSRLS makes every policy in this file a no-op");

            var ownedTables = await ScalarAsync<long>(
                connection,
                """
                SELECT count(*) FROM pg_tables
                WHERE schemaname = 'public' AND tableowner = current_user
                """);
            ownedTables.Should().Be(0, "a table's owner is not subject to its own policies");
        }
    }

    /// <summary>
    /// Append-only is a grant, not a convention.
    /// </summary>
    [Theory]
    [InlineData("UPDATE config_versions SET message = 'rewritten'")]
    [InlineData("DELETE FROM config_versions")]
    [InlineData("UPDATE audit_events SET action = 'rewritten'")]
    [InlineData("DELETE FROM audit_events")]
    [InlineData("DELETE FROM users")]
    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "The statement is a compile-time constant from this file. The rule is kept "
            + "at error severity repository-wide because it guards the generator's process arguments; "
            + "there is no external input anywhere on this path.")]
    public async Task Append_only_tables_refuse_rewrites(string statement)
    {
        var tenant = await TenantSeed.CreateAsync(fixture, "append");

        var connection = await fixture.OpenAsAppAsync(tenant.UserId);
        await using (connection.ConfigureAwait(false))
        {
            var command = new NpgsqlCommand(statement, connection);
            await using (command.ConfigureAwait(false))
            {
                var execute = async () => await command.ExecuteNonQueryAsync();

                (await execute.Should().ThrowAsync<PostgresException>())
                    .Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
            }
        }
    }

    /// <summary>
    /// Every table is either policed or on a short, named list of exemptions.
    /// </summary>
    /// <remarks>
    /// ⚠️ This is the "somebody added a table and forgot" test. Without it, the
    /// failure mode is a new tenant-scoped table with no policy, which behaves
    /// perfectly in every test that reads it as a single tenant. Adding a table
    /// now forces a decision here, in a diff a reviewer sees.
    /// </remarks>
    [Fact]
    public async Task Every_table_is_policed_or_explicitly_exempt()
    {
        // Every exemption is a decision with a reason, not a list that grew.
        //
        // All five are credential or identity tables, and none of them is
        // tenant-scoped: an account, a session, and a reset link belong to a
        // person, who may be a member of several organisations or of none. A
        // membership predicate over them would not be stricter, it would be
        // meaningless — there is no tenant to compare against.
        //
        // What protects them instead is narrower than a policy: refresh_tokens,
        // user_tokens and oauth_identities are only ever reached by an indexed
        // lookup on a 256-bit secret the caller had to already possess, and
        // there is no endpoint that lists them. api_tokens *is* org-scoped and
        // does carry a policy, which is the contrast worth noticing.
        var exempt = new HashSet<string>(StringComparer.Ordinal)
        {
            "users",
            "refresh_tokens",
            "user_tokens",
            "oauth_identities",
            "__migrations",
        };

        var connection = await fixture.OpenAsOwnerAsync();
        await using (connection.ConfigureAwait(false))
        {
            var command = new NpgsqlCommand(
                """
                SELECT c.relname, c.relrowsecurity
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'public' AND c.relkind = 'r'
                ORDER BY c.relname
                """,
                connection);

            await using (command.ConfigureAwait(false))
            {
                var unpoliced = new List<string>();
                var reader = await command.ExecuteReaderAsync();
                await using (reader.ConfigureAwait(false))
                {
                    while (await reader.ReadAsync())
                    {
                        var table = reader.GetString(0);
                        var policed = reader.GetBoolean(1);

                        if (!policed && !exempt.Contains(table))
                        {
                            unpoliced.Add(table);
                        }
                    }
                }

                unpoliced.Should().BeEmpty(
                    "every new table needs either a row-level security policy or an entry in this test's exemption list");
            }
        }
    }

    private async Task<List<string>> AppNamesAsync(Guid? userId)
    {
        var names = new List<string>();

        var connection = await fixture.OpenAsAppAsync(userId);
        await using (connection.ConfigureAwait(false))
        {
            var command = new NpgsqlCommand("SELECT name FROM apps", connection);
            await using (command.ConfigureAwait(false))
            {
                var reader = await command.ExecuteReaderAsync();
                await using (reader.ConfigureAwait(false))
                {
                    while (await reader.ReadAsync())
                    {
                        names.Add(reader.GetString(0));
                    }
                }
            }
        }

        return names;
    }

    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "The statement is a compile-time constant from this file. The rule is kept "
            + "at error severity repository-wide because it guards the generator's process arguments; "
            + "there is no external input anywhere on this path.")]
    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string sql)
    {
        var command = new NpgsqlCommand(sql, connection);
        await using (command.ConfigureAwait(false))
        {
            return (T)(await command.ExecuteScalarAsync())!;
        }
    }
}
