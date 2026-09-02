using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Npgsql;
using Shellwright.Orchestrator.Persistence;
using Shellwright.Orchestrator.Tests.Infrastructure;
using Shellwright.Orchestrator.Workflows;
using Xunit;

namespace Shellwright.Orchestrator.Tests;

/// <summary>
/// TC-S07-BLD-076–089 — the build record and the cache, against a real database.
/// </summary>
/// <remarks>
/// ⚠️ What is under test here is mostly PostgreSQL's behaviour, which is why it
/// is PostgreSQL under it: ON CONFLICT making a retry a no-op rather than a
/// second charge, FOR UPDATE serialising two activities that report at once,
/// and the runner role's grants refusing what the orchestrator must not do.
/// Each of those passes trivially against an in-memory fake and is the exact
/// thing that would break in production.
/// </remarks>
/// <param name="fixture">The migrated database.</param>
[Collection(BuildDatabaseFixtureDefinition.Name)]
public sealed class PostgresStoreTests(BuildDatabaseFixture fixture)
{
    [Fact(DisplayName = "A configuration comes back with its app and body")]
    public async Task LoadsAConfiguration()
    {
        var seed = await SeedAsync();

        var stored = await Store().LoadConfigAsync(seed.ConfigVersionId);

        stored.Should().NotBeNull();
        stored!.AppId.Should().Be(seed.AppId);
        stored.Body["app"]!["name"]!.GetValue<string>().Should().Be("seeded");
    }

    [Fact(DisplayName = "A configuration that does not exist is null, not an exception")]
    public async Task MissingConfigurationIsNull() =>
        (await Store().LoadConfigAsync(Guid.CreateVersion7())).Should().BeNull();

    [Fact(DisplayName = "A legal transition moves the build and appends to its history")]
    public async Task RecordsATransition()
    {
        var seed = await SeedAsync();
        var store = Store();

        await store.RecordTransitionAsync(seed.BuildId, BuildState.Generating);
        await store.RecordTransitionAsync(seed.BuildId, BuildState.Building);

        (await StateAsync(seed.BuildId)).Should().Be(BuildState.Building);
        (await TransitionsAsync(seed.BuildId))
            .Should().Equal(BuildState.Generating, BuildState.Building);
    }

    [Fact(DisplayName = "An illegal transition is refused")]
    public async Task RefusesAnIllegalTransition()
    {
        var seed = await SeedAsync();
        var store = Store();

        await store.RecordTransitionAsync(seed.BuildId, BuildState.Generating);

        // Queued is behind Generating. A build cannot go back.
        var act = () => store.RecordTransitionAsync(seed.BuildId, BuildState.Queued);

        await act.Should().ThrowAsync<IllegalBuildTransitionException>();
        (await StateAsync(seed.BuildId)).Should().Be(BuildState.Generating);
    }

    [Fact(DisplayName = "Recording the same transition twice is a retry, not an illegal move")]
    public async Task RepeatingATransitionIsANoOp()
    {
        var seed = await SeedAsync();
        var store = Store();

        await store.RecordTransitionAsync(seed.BuildId, BuildState.Generating);

        // ⚠️ Temporal replays activities. Treating the second report as an
        // illegal transition would fail builds for succeeding.
        await store.RecordTransitionAsync(seed.BuildId, BuildState.Generating);

        (await TransitionsAsync(seed.BuildId)).Should().ContainSingle();
    }

    [Fact(DisplayName = "Reaching a terminal state stamps when the build finished")]
    public async Task StampsTimestampsFromTheState()
    {
        var seed = await SeedAsync();
        var store = Store();

        (await TimestampsAsync(seed.BuildId)).Started.Should().BeNull("a queued build has not started");

        await store.RecordTransitionAsync(seed.BuildId, BuildState.Generating);

        var running = await TimestampsAsync(seed.BuildId);
        running.Started.Should().NotBeNull();
        running.Finished.Should().BeNull();

        await store.RecordTransitionAsync(seed.BuildId, BuildState.Building);
        await store.RecordTransitionAsync(seed.BuildId, BuildState.Verifying);
        await store.RecordTransitionAsync(seed.BuildId, BuildState.Succeeded);

        var done = await TimestampsAsync(seed.BuildId);
        done.Finished.Should().NotBeNull();

        // Stamped from the state rather than by the caller, so "how long did
        // this take" cannot disagree with "what happened".
        done.Started.Should().Be(running.Started);
    }

    [Fact(DisplayName = "Metering the same build twice charges once")]
    public async Task UsageIsIdempotent()
    {
        var seed = await SeedAsync();
        var store = Store();
        var usage = new UsageRecord(seed.OrgId, seed.BuildId, BuildPlatform.Android, 240, false, 8_000_000);

        await store.RecordUsageAsync(usage);

        // ⚠️ The retry Temporal actually produces: the row was committed and
        // then the acknowledgement was lost. Without ON CONFLICT this second
        // call either throws and fails a successful build, or bills twice.
        await store.RecordUsageAsync(usage);

        (await UsageRowsAsync(seed.BuildId)).Should().Be(1);
    }

    [Fact(DisplayName = "A failure reason longer than the column is truncated, not dropped")]
    public async Task LongFailureReasonsAreTruncated()
    {
        var seed = await SeedAsync();

        var reason = new string('x', 5_000);
        await Store().RecordFailureAsync(seed.BuildId, new BuildFailure("compilation_failed", reason));

        // The build already failed. Losing why it failed is not an improvement
        // on losing the last three thousand characters of why.
        var stored = await ScalarAsync<string>("SELECT failure_message FROM builds WHERE id = @id", seed.BuildId);
        stored.Should().HaveLength(2000);
    }

    [Fact(DisplayName = "A cache miss is a miss")]
    public async Task CacheMiss()
    {
        var seed = await SeedAsync();

        var lookup = await Cache().LookupAsync(seed.AppId, BuildPlatform.Android, BuildType.Debug, Hashes());

        lookup.Kind.Should().Be(CacheOutcome.Miss);
    }

    [Fact(DisplayName = "All three keys matching is a complete hit")]
    public async Task CacheCompleteHit()
    {
        var seed = await SeedAsync();
        var cache = Cache();
        var hashes = Hashes();

        await cache.StoreAsync(
            seed.AppId,
            BuildPlatform.Android,
            BuildType.Debug,
            hashes,
            new UploadedArtifact("artifact://sha256-" + new string('1', 64), 4321));

        var lookup = await cache.LookupAsync(seed.AppId, BuildPlatform.Android, BuildType.Debug, hashes);

        lookup.Kind.Should().Be(CacheOutcome.Complete);
        lookup.ArtifactBytes.Should().Be(4321);
    }

    [Fact(DisplayName = "Only the content key moving is a patchable hit")]
    public async Task CachePatchHit()
    {
        var seed = await SeedAsync();
        var cache = Cache();

        await cache.StoreAsync(
            seed.AppId,
            BuildPlatform.Android,
            BuildType.Debug,
            Hashes(),
            new UploadedArtifact("artifact://sha256-" + new string('2', 64), 4321));

        var lookup = await cache.LookupAsync(
            seed.AppId,
            BuildPlatform.Android,
            BuildType.Debug,
            Hashes(content: new string('z', 64)));

        lookup.Kind.Should().Be(CacheOutcome.Patch);
        lookup.ArtifactReference.Should().NotBeNull("the patcher needs something to patch");
    }

    [Fact(DisplayName = "The asset key moving is warm, and offers no artifact to patch")]
    public async Task CacheWarmHit()
    {
        var seed = await SeedAsync();
        var cache = Cache();

        await cache.StoreAsync(
            seed.AppId,
            BuildPlatform.Android,
            BuildType.Debug,
            Hashes(),
            new UploadedArtifact("artifact://sha256-" + new string('3', 64), 4321));

        var lookup = await cache.LookupAsync(
            seed.AppId,
            BuildPlatform.Android,
            BuildType.Debug,
            Hashes(asset: new string('y', 64), content: new string('z', 64)));

        lookup.Kind.Should().Be(CacheOutcome.Warm);

        // ⚠️ Null on purpose. An icon or a colour is a compiled resource, so the
        // cached artifact's resources are stale — handing back a reference here
        // would invite a caller to patch it and ship the old icon.
        lookup.ArtifactReference.Should().BeNull();
    }

    [Fact(DisplayName = "A complete match outranks a partial one, whatever order they were stored in")]
    public async Task BestMatchWins()
    {
        var seed = await SeedAsync();
        var cache = Cache();

        // The partial one first, so ordering by insertion would pick it.
        await cache.StoreAsync(
            seed.AppId,
            BuildPlatform.Android,
            BuildType.Debug,
            Hashes(content: new string('z', 64)),
            new UploadedArtifact("artifact://sha256-" + new string('4', 64), 1111));

        await cache.StoreAsync(
            seed.AppId,
            BuildPlatform.Android,
            BuildType.Debug,
            Hashes(),
            new UploadedArtifact("artifact://sha256-" + new string('5', 64), 2222));

        var lookup = await cache.LookupAsync(seed.AppId, BuildPlatform.Android, BuildType.Debug, Hashes());

        lookup.Kind.Should().Be(CacheOutcome.Complete);
        lookup.ArtifactBytes.Should().Be(2222);
    }

    [Fact(DisplayName = "A debug artifact never satisfies a release build")]
    public async Task DebugNeverSatisfiesRelease()
    {
        var seed = await SeedAsync();
        var cache = Cache();
        var hashes = Hashes();

        await cache.StoreAsync(
            seed.AppId,
            BuildPlatform.Android,
            BuildType.Debug,
            hashes,
            new UploadedArtifact("artifact://sha256-" + new string('6', 64), 4321));

        // ⚠️ Would hand a customer an unpublishable binary in answer to a
        // request for a publishable one.
        var lookup = await cache.LookupAsync(seed.AppId, BuildPlatform.Android, BuildType.Release, hashes);

        lookup.Kind.Should().Be(CacheOutcome.Miss);
    }

    [Fact(DisplayName = "An android artifact never satisfies an ios build")]
    public async Task OnePlatformNeverSatisfiesAnother()
    {
        var seed = await SeedAsync();
        var cache = Cache();
        var hashes = Hashes();

        await cache.StoreAsync(
            seed.AppId,
            BuildPlatform.Android,
            BuildType.Debug,
            hashes,
            new UploadedArtifact("artifact://sha256-" + new string('7', 64), 4321));

        (await cache.LookupAsync(seed.AppId, BuildPlatform.Ios, BuildType.Debug, hashes))
            .Kind.Should().Be(CacheOutcome.Miss);
    }

    [Fact(DisplayName = "Storing the same artifact twice does not duplicate the entry")]
    public async Task StoringTwiceIsIdempotent()
    {
        var seed = await SeedAsync();
        var cache = Cache();
        var hashes = Hashes();
        var artifact = new UploadedArtifact("artifact://sha256-" + new string('8', 64), 4321);

        await cache.StoreAsync(seed.AppId, BuildPlatform.Android, BuildType.Debug, hashes, artifact);
        await cache.StoreAsync(seed.AppId, BuildPlatform.Android, BuildType.Debug, hashes, artifact);

        var rows = await ScalarAsync<long>(
            "SELECT count(*) FROM artifact_cache WHERE app_id = @id",
            seed.AppId);

        rows.Should().Be(1);
    }

    private static BuildHashes Hashes(string? asset = null, string? content = null) =>
        new(
            new string('c', 64),
            asset ?? new string('a', 64),
            content ?? new string('e', 64));

    private PostgresBuildStore Store() =>
        new(Options.Create(new BuildStoreOptions { ConnectionString = fixture.RunnerConnectionString }));

    private PostgresArtifactCache Cache() =>
        new(Options.Create(new BuildStoreOptions { ConnectionString = fixture.RunnerConnectionString }));

    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "Every statement reaching this helper is a compile-time constant from this file.")]
    private async Task<T> ScalarAsync<T>(string sql, Guid id)
    {
        var connection = await fixture.OpenAsRunnerAsync();
        await using (connection.ConfigureAwait(false))
        {
            var command = new NpgsqlCommand(sql, connection);
            await using (command.ConfigureAwait(false))
            {
                command.Parameters.AddWithValue("id", id);
                return (T)(await command.ExecuteScalarAsync())!;
            }
        }
    }

    private async Task<BuildState> StateAsync(Guid buildId) =>
        (BuildState)await ScalarAsync<int>("SELECT state FROM builds WHERE id = @id", buildId);

    private async Task<long> UsageRowsAsync(Guid buildId) =>
        await ScalarAsync<long>("SELECT count(*) FROM usage_records WHERE build_id = @id", buildId);

    private async Task<(DateTime? Started, DateTime? Finished)> TimestampsAsync(Guid buildId)
    {
        var connection = await fixture.OpenAsRunnerAsync();
        await using (connection.ConfigureAwait(false))
        {
            var command = new NpgsqlCommand(
                "SELECT started_at, finished_at FROM builds WHERE id = @id",
                connection);

            await using (command.ConfigureAwait(false))
            {
                command.Parameters.AddWithValue("id", buildId);

                var reader = await command.ExecuteReaderAsync();
                await using (reader.ConfigureAwait(false))
                {
                    (await reader.ReadAsync()).Should().BeTrue();

                    return (
                        await reader.IsDBNullAsync(0) ? null : reader.GetDateTime(0),
                        await reader.IsDBNullAsync(1) ? null : reader.GetDateTime(1));
                }
            }
        }
    }

    private async Task<List<BuildState>> TransitionsAsync(Guid buildId)
    {
        var states = new List<BuildState>();

        var connection = await fixture.OpenAsRunnerAsync();
        await using (connection.ConfigureAwait(false))
        {
            var command = new NpgsqlCommand(
                "SELECT state FROM build_transitions WHERE build_id = @id ORDER BY occurred_at, id",
                connection);

            await using (command.ConfigureAwait(false))
            {
                command.Parameters.AddWithValue("id", buildId);

                var reader = await command.ExecuteReaderAsync();
                await using (reader.ConfigureAwait(false))
                {
                    while (await reader.ReadAsync())
                    {
                        states.Add((BuildState)reader.GetInt32(0));
                    }
                }
            }
        }

        return states;
    }

    private async Task<Seeded> SeedAsync()
    {
        var seeded = new Seeded(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7());

        var suffix = Guid.NewGuid().ToString("N")[..8];

        var connection = await fixture.OpenAsOwnerAsync();
        await using (connection.ConfigureAwait(false))
        {
            var command = new NpgsqlCommand(
                """
                INSERT INTO orgs (id, name, slug, plan, created_at)
                VALUES (@org, @label, @slug, 'Free', now());

                INSERT INTO workspaces (id, org_id, name, slug, created_at)
                VALUES (@workspace, @org, @label, 'default', now());

                INSERT INTO apps (id, workspace_id, name, bundle_id, created_at)
                VALUES (@app, @workspace, @label, @bundle, now());

                INSERT INTO config_versions
                    (id, app_id, schema_version, body, code_key, asset_key, content_key, created_at)
                VALUES (@config, @app, 1, @body::jsonb, @code, @asset, @content, now());

                INSERT INTO builds
                    (id, app_id, org_id, config_version_id, platform, type, state,
                     workflow_id, cache_outcome, runner_seconds, idempotency_key, created_at)
                VALUES (@build, @app, @org, @config, 0, 0, 0, @workflow, 0, 0, @key, now());
                """,
                connection);

            await using (command.ConfigureAwait(false))
            {
                command.Parameters.AddWithValue("org", seeded.OrgId);
                command.Parameters.AddWithValue("workspace", seeded.WorkspaceId);
                command.Parameters.AddWithValue("app", seeded.AppId);
                command.Parameters.AddWithValue("config", seeded.ConfigVersionId);
                command.Parameters.AddWithValue("build", seeded.BuildId);
                command.Parameters.AddWithValue("label", $"store {suffix}");
                command.Parameters.AddWithValue("slug", $"store-{suffix}");
                command.Parameters.AddWithValue("bundle", $"test.store.s{suffix}");
                command.Parameters.AddWithValue("workflow", $"store-{suffix}");
                command.Parameters.AddWithValue("key", Guid.NewGuid().ToString("N"));
                command.Parameters.AddWithValue("body", """{"app":{"name":"seeded"}}""");
                command.Parameters.AddWithValue("code", new string('c', 64));
                command.Parameters.AddWithValue("asset", new string('a', 64));
                command.Parameters.AddWithValue("content", new string('e', 64));

                await command.ExecuteNonQueryAsync();
            }
        }

        return seeded;
    }

    private sealed record Seeded(
        Guid OrgId,
        Guid WorkspaceId,
        Guid AppId,
        Guid ConfigVersionId,
        Guid BuildId,
        Guid Unused);
}
