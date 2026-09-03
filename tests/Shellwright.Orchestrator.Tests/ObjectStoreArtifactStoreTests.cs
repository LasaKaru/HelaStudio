using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Shellwright.Orchestrator.Artifacts;
using Shellwright.Orchestrator.Tests.Infrastructure;
using Shellwright.Orchestrator.Workflows;
using Xunit;

namespace Shellwright.Orchestrator.Tests;

/// <summary>
/// TC-S08-BLD-029–038 — artifacts through an S3-compatible endpoint.
/// </summary>
/// <remarks>
/// ⚠️ Against a real HTTP endpoint rather than a mocked <c>IAmazonS3</c>. A
/// mock would confirm we call the methods we think we call and would say
/// nothing about whether the SDK can reach a path-style endpoint, whether the
/// request it builds is well formed, or whether the bytes survive — which are
/// the things that actually break against R2. What is still untested is R2
/// itself, which needs credentials this project does not have.
/// </remarks>
public sealed class ObjectStoreArtifactStoreTests : IDisposable
{
    private readonly FakeObjectStore endpoint = new();
    private readonly List<IDisposable> clients = [];

    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"shellwright-object-{Guid.NewGuid():N}");

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
        foreach (var client in clients)
        {
            client.Dispose();
        }

        endpoint.Dispose();

        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact(DisplayName = "An artifact round-trips through object storage byte for byte")]
    public async Task RoundTrips()
    {
        var store = Store();
        var source = await WriteArtifactAsync(300_000);

        var uploaded = await store.StoreAsync(request, source);

        var destination = Path.Combine(root, "fetched.apk");
        var bytes = await store.FetchAsync(uploaded.ArtifactReference, destination);

        bytes.Should().Be(new FileInfo(source).Length);
        (await File.ReadAllBytesAsync(destination))
            .Should().Equal(await File.ReadAllBytesAsync(source));
    }

    [Fact(DisplayName = "The reference is the same content address the filesystem store uses")]
    public async Task ReferenceIsInterchangeable()
    {
        var source = await WriteArtifactAsync(20_000);

        var uploaded = await Store().StoreAsync(request, source);

        await using var reading = File.OpenRead(source);
        var digest = Convert.ToHexStringLower(await SHA256.HashDataAsync(reading));

        // ⚠️ Identical to what FileSystemArtifactStore would have produced, so
        // the two backends are interchangeable and a deployment can move
        // between them without rewriting a single row of the builds table.
        // A URL-shaped reference would have baked one backend into all of them.
        uploaded.ArtifactReference.Should().Be(FileSystemArtifactStore.Reference(digest));
    }

    [Fact(DisplayName = "The object key fans out rather than flooding one prefix")]
    public async Task KeysFanOut()
    {
        var source = await WriteArtifactAsync(20_000);
        var uploaded = await Store().StoreAsync(request, source);
        var digest = FileSystemArtifactStore.DigestOf(uploaded.ArtifactReference);

        endpoint.Keys.Should().ContainSingle()
            .Which.Should().Be($"artifacts/{digest[..2]}/{digest[2..4]}/{digest}");
    }

    [Fact(DisplayName = "Storing identical bytes twice does not upload twice")]
    public async Task IdenticalArtifactsUploadOnce()
    {
        var store = Store();

        var first = await WriteArtifactAsync(50_000, name: "first");
        var second = Path.Combine(root, "second.apk");
        File.Copy(first, second);

        var a = await store.StoreAsync(request, first);
        var b = await store.StoreAsync(request, second);

        a.ArtifactReference.Should().Be(b.ArtifactReference);

        // ⚠️ The saving is the upload, not the storage. An artifact is tens of
        // megabytes over somebody's network, and two builds of one
        // configuration is the common case rather than the exotic one.
        endpoint.Keys.Should().ContainSingle();
    }

    [Fact(DisplayName = "Fetching something that was never stored fails loudly")]
    public async Task MissingObjectThrows()
    {
        var reference = FileSystemArtifactStore.Reference(new string('0', 64));

        var act = () => Store().FetchAsync(reference, Path.Combine(root, "out.apk"));

        // Mapped to the same exception the filesystem store raises, so callers
        // do not have to know which backend they are on.
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact(DisplayName = "A reference that tries to escape the bucket is refused")]
    public async Task TraversalIsRefused()
    {
        var store = Store();

        foreach (var attempt in new[]
        {
            "artifact://sha256-../../../../etc/passwd",
            "artifact://sha256-" + new string('A', 64),
            "artifact://sha256-",
            "https://evil.example/x",
        })
        {
            var act = () => store.FetchAsync(attempt, Path.Combine(root, "out.bin"));

            await act.Should().ThrowAsync<ArgumentException>(
                "'{0}' is not a valid artifact reference",
                attempt);
        }
    }

    [Fact(DisplayName = "An artifact over the size limit is refused before it is uploaded")]
    public async Task OversizeIsRefusedBeforeUpload()
    {
        var store = Store(maxBytes: 100_000);
        var source = await WriteArtifactAsync(200_000);

        var act = () => store.StoreAsync(request, source);

        await act.Should().ThrowAsync<InvalidOperationException>();

        // ⚠️ Nothing was sent. Refusing after the upload would mean paying to
        // transfer an artifact in order to reject it.
        endpoint.Keys.Should().BeEmpty();
    }

    [Fact(DisplayName = "A build that produced nothing fails without touching the network")]
    public async Task MissingArtifactThrows()
    {
        var act = () => Store().StoreAsync(request, Path.Combine(root, "never-written.apk"));

        await act.Should().ThrowAsync<FileNotFoundException>();
        endpoint.Keys.Should().BeEmpty();
    }

    [Fact(DisplayName = "Every request carries credentials")]
    public async Task RequestsAreSigned()
    {
        var store = Store();
        var uploaded = await store.StoreAsync(request, await WriteArtifactAsync(20_000));
        await store.FetchAsync(uploaded.ArtifactReference, Path.Combine(root, "out.apk"));

        // ⚠️ Presence, not validity: reimplementing SigV4 here to check our own
        // signing would agree with our own mistakes. What this catches is a
        // client configured with no credentials at all, which is the failure
        // that otherwise appears as a 403 from R2 in production.
        endpoint.UnauthenticatedRequests.Should().Be(0);
    }

    [Fact(DisplayName = "A large artifact streams rather than being buffered whole")]
    public async Task LargeArtifactsStream()
    {
        var store = Store();

        // Sixty megabytes: bigger than any buffer worth allocating, small
        // enough that the test stays quick.
        var source = await WriteArtifactAsync(60 * 1024 * 1024);

        var uploaded = await store.StoreAsync(request, source);
        var destination = Path.Combine(root, "big.ipa");

        var bytes = await store.FetchAsync(uploaded.ArtifactReference, destination);

        bytes.Should().Be(60L * 1024 * 1024);
        new FileInfo(destination).Length.Should().Be(60L * 1024 * 1024);
    }

    private ObjectStoreArtifactStore Store(long maxBytes = 2_000_000_000)
    {
        var settings = new ObjectStorageOptions
        {
            ServiceUrl = endpoint.ServiceUrl,
            Bucket = "shellwright-artifacts",
            AccessKeyId = "test-access-key",
            SecretAccessKey = "test-secret-key",
            MaxArtifactBytes = maxBytes,
        };

        var client = ObjectStoreClientFactory.Create(settings);
        clients.Add(client);

        return new ObjectStoreArtifactStore(client, Options.Create(settings));
    }

    private async Task<string> WriteArtifactAsync(int bytes, string name = "artifact")
    {
        Directory.CreateDirectory(root);

        var path = Path.Combine(root, name + ".apk");
        var content = new byte[bytes];

        // Deterministic, so a failure reproduces.
        for (var index = 0; index < content.Length; index++)
        {
            content[index] = (byte)(index % 251);
        }

        await File.WriteAllBytesAsync(path, content);

        return path;
    }
}
