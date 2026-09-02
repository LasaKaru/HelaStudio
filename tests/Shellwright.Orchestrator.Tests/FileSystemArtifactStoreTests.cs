using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Shellwright.Orchestrator.Artifacts;
using Shellwright.Orchestrator.Workflows;
using Xunit;

namespace Shellwright.Orchestrator.Tests;

/// <summary>
/// TC-S07-BLD-040–047 — artifacts are stored by their own content, and come back.
/// </summary>
/// <remarks>
/// Against a real directory. The behaviour that matters — that identical bytes
/// cost one copy, that a reference cannot be made to point outside the store,
/// that a fetch returns the bytes that went in — is filesystem behaviour, and a
/// fake store would agree with whatever these tests claimed.
/// </remarks>
public sealed class FileSystemArtifactStoreTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"shellwright-artifacts-{Guid.NewGuid():N}");

    private readonly BuildRequest request = new(
        BuildId: Guid.NewGuid(),
        OrgId: Guid.NewGuid(),
        AppId: Guid.NewGuid(),
        ConfigVersionId: Guid.NewGuid(),
        Platform: BuildPlatform.Android,
        Type: BuildType.Debug);

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact(DisplayName = "An artifact round-trips byte for byte")]
    public async Task RoundTrips()
    {
        var store = Store();
        var source = await WriteArtifactAsync("the-apk", 300_000);

        var uploaded = await store.StoreAsync(request, source);

        var destination = Path.Combine(root, "fetched.apk");
        var bytes = await store.FetchAsync(uploaded.ArtifactReference, destination);

        bytes.Should().Be(new FileInfo(source).Length);
        (await File.ReadAllBytesAsync(destination))
            .Should().Equal(await File.ReadAllBytesAsync(source));
    }

    [Fact(DisplayName = "The reference is the artifact's own SHA-256")]
    public async Task ReferenceIsTheContentDigest()
    {
        var source = await WriteArtifactAsync("the-apk", 20_000);

        var uploaded = await Store().StoreAsync(request, source);

        await using var reading = File.OpenRead(source);
        var expected = Convert.ToHexStringLower(await SHA256.HashDataAsync(reading));

        uploaded.ArtifactReference.Should().Be(FileSystemArtifactStore.ReferenceScheme + expected);
    }

    [Fact(DisplayName = "Two builds producing identical bytes cost one copy")]
    public async Task IdenticalArtifactsAreStoredOnce()
    {
        var store = Store();

        var first = await WriteArtifactAsync("first", 50_000);
        var second = Path.Combine(root, "second.apk");
        File.Copy(first, second);

        var a = await store.StoreAsync(request, first);
        var b = await store.StoreAsync(request, second);

        a.ArtifactReference.Should().Be(b.ArtifactReference);

        Directory.GetFiles(Path.Combine(root, "store"), "*", SearchOption.AllDirectories)
            .Should().ContainSingle("identical bytes must not be stored twice");
    }

    [Fact(DisplayName = "A reference that tries to escape the store is refused")]
    public async Task TraversalIsRefused()
    {
        var store = Store();

        // ⚠️ A reference reaches the store from a database row and is turned
        // into a path. If a digest could carry separators, a row could name any
        // file the orchestrator can read.
        var attempts = new[]
        {
            "artifact://sha256-../../../../etc/passwd",
            "artifact://sha256-" + new string('a', 63) + "/",
            "artifact://sha256-" + new string('A', 64),
            "artifact://sha256-",
            "/etc/passwd",
        };

        foreach (var attempt in attempts)
        {
            var act = () => store.FetchAsync(attempt, Path.Combine(root, "out.bin"));

            // ⚠️ Awaited. ThrowAsync returns a task, and a forgotten await here
            // makes the whole loop pass regardless of what the store does.
            await act.Should().ThrowAsync<ArgumentException>(
                "'{0}' is not a valid artifact reference",
                attempt);
        }
    }

    [Fact(DisplayName = "An artifact over the size limit is refused rather than stored")]
    public async Task OversizeIsRefused()
    {
        var store = Store(maxBytes: 1_000_000);
        var source = await WriteArtifactAsync("huge", 1_500_000);

        var act = () => store.StoreAsync(request, source);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact(DisplayName = "A build that reported success but produced nothing fails loudly")]
    public async Task MissingArtifactThrows()
    {
        var act = () => Store().StoreAsync(request, Path.Combine(root, "never-written.apk"));

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact(DisplayName = "Fetching an artifact that was never stored fails loudly")]
    public async Task MissingFetchThrows()
    {
        var reference = FileSystemArtifactStore.Reference(new string('0', 64));

        var act = () => Store().FetchAsync(reference, Path.Combine(root, "out.apk"));

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact(DisplayName = "No temporary staging file survives a completed store")]
    public async Task StagingIsCleanedUp()
    {
        var store = Store();
        await store.StoreAsync(request, await WriteArtifactAsync("the-apk", 40_000));

        Directory.GetFiles(Path.Combine(root, "store"), "*.tmp", SearchOption.AllDirectories)
            .Should().BeEmpty();
    }

    private FileSystemArtifactStore Store(long maxBytes = 2_000_000_000) =>
        new(Options.Create(new ArtifactStorageOptions
        {
            Directory = Path.Combine(root, "store"),
            MaxArtifactBytes = maxBytes,
        }));

    private async Task<string> WriteArtifactAsync(string name, int bytes)
    {
        Directory.CreateDirectory(root);

        var path = Path.Combine(root, name + ".apk");
        var content = new byte[bytes];

        // Deterministic rather than random, so a failure reproduces.
        for (var index = 0; index < content.Length; index++)
        {
            content[index] = (byte)(index % 251);
        }

        await File.WriteAllBytesAsync(path, content);

        return path;
    }
}
