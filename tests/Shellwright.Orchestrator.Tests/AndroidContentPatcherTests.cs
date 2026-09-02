using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Shellwright.Orchestrator.Artifacts;
using Shellwright.Orchestrator.Patching;
using Shellwright.Orchestrator.Sandbox;
using Shellwright.Orchestrator.Workflows;
using Xunit;

namespace Shellwright.Orchestrator.Tests;

/// <summary>
/// TC-S07-BLD-048–056 — the content fast path produces a real APK, or refuses.
/// </summary>
/// <remarks>
/// ⚠️ The zip work is done for real, against real archives on disk, because
/// every failure this path can have is a zip-level one: the wrong entry
/// replaced, the old signature left behind, an entry silently dropped. The
/// align and sign steps are asserted at the argument level — no Android
/// toolchain exists in this environment, and that gap is recorded rather than
/// papered over with a mock that always succeeds.
/// </remarks>
public sealed class AndroidContentPatcherTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"shellwright-patch-{Guid.NewGuid():N}");

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

    [Fact(DisplayName = "The patched APK carries the new configuration and keeps everything else")]
    public async Task ReplacesOnlyTheConfiguration()
    {
        var (patcher, sandbox, cached) = await ArrangeAsync();

        var config = new JsonObject { ["app"] = new JsonObject { ["initialUrl"] = "https://after.example" } };

        var built = await patcher.PatchAsync(request, Lease(), cached, config, NoLogging);

        // Nothing was compiled, so the aligned file is what the caller is given.
        built.WasPatched.Should().BeTrue();
        sandbox.Commands.Select(command => command.Executable)
            .Should().Equal("zipalign", "apksigner");

        // The aligned output is produced by zipalign, which did not really run
        // here — so the archive to inspect is the one this class actually built.
        using var patched = await ZipFile.OpenReadAsync(PatchedPath());

        var written = await ReadEntryAsync(patched, AndroidContentPatcher.ConfigEntryPath);
        written.Should().Contain("after.example");
        written.Should().NotContain("before.example");

        (await ReadEntryAsync(patched, "AndroidManifest.xml"))
            .Should().Be("compiled-manifest");
        (await ReadEntryAsync(patched, "classes.dex"))
            .Should().Be("dex-bytes");
        (await ReadEntryAsync(patched, "res/drawable/icon.png"))
            .Should().Be("icon-bytes");
    }

    [Fact(DisplayName = "The previous signature is removed, manifest included")]
    public async Task DropsTheOldSignature()
    {
        var (patcher, _, cached) = await ArrangeAsync();

        await patcher.PatchAsync(request, Lease(), cached, new JsonObject(), NoLogging);

        using var patched = await ZipFile.OpenReadAsync(PatchedPath());

        // ⚠️ An APK whose contents changed but whose META-INF still claims the
        // old digests is not a signed APK — it is a corrupt one that some tools
        // install and others reject.
        patched.Entries.Select(entry => entry.FullName)
            .Should().NotContain([
                "META-INF/MANIFEST.MF",
                "META-INF/CERT.SF",
                "META-INF/CERT.RSA",
            ]);
    }

    [Fact(DisplayName = "The configuration is written as the same canonical bytes the cache key is computed from")]
    public async Task WritesCanonicalBytes()
    {
        var (patcher, _, cached) = await ArrangeAsync();

        // Keys out of order and spacing that canonicalisation must remove. If
        // the patcher wrote this verbatim, the APK would sit in the cache under
        // a content key its own contents do not produce, and the next identical
        // request would miss.
        var config = JsonNode.Parse("""{ "b": 2,   "a": 1 }""")!.AsObject();

        await patcher.PatchAsync(request, Lease(), cached, config, NoLogging);

        using var patched = await ZipFile.OpenReadAsync(PatchedPath());

        (await ReadEntryAsync(patched, AndroidContentPatcher.ConfigEntryPath))
            .Should().Be("""{"a":1,"b":2}""");
    }

    [Fact(DisplayName = "An artifact with no configuration asset is refused, not patched")]
    public async Task RefusesAnArtifactItCannotPatch()
    {
        var (patcher, sandbox, cached) = await ArrangeAsync(withConfigEntry: false);

        var act = () => patcher.PatchAsync(request, Lease(), cached, new JsonObject(), NoLogging);

        await act.Should().ThrowAsync<PatchNotPossibleException>();

        // ⚠️ And nothing was signed. A patcher that gets as far as apksigner on
        // an artifact it could not patch has already produced something.
        sandbox.Commands.Should().BeEmpty();
    }

    [Fact(DisplayName = "A patchable hit with no artifact to patch is refused")]
    public async Task RefusesAHitWithNoArtifact()
    {
        var (patcher, _, _) = await ArrangeAsync();

        var act = () => patcher.PatchAsync(
            request,
            Lease(),
            new CacheLookup(CacheOutcome.Patch, null, 0),
            new JsonObject(),
            NoLogging);

        await act.Should().ThrowAsync<PatchNotPossibleException>();
    }

    [Fact(DisplayName = "iOS is not patched by the Android patcher")]
    public async Task RefusesAnotherPlatform()
    {
        var (patcher, _, cached) = await ArrangeAsync();

        patcher.Supports(BuildPlatform.Ios).Should().BeFalse();

        var act = () => patcher.PatchAsync(
            request with { Platform = BuildPlatform.Ios },
            Lease(),
            cached,
            new JsonObject(),
            NoLogging);

        await act.Should().ThrowAsync<PatchNotPossibleException>();
    }

    [Fact(DisplayName = "A signing tool that fails is a failure, not a quiet fallback")]
    public async Task ASigningFailureSurfaces()
    {
        var (patcher, _, cached) = await ArrangeAsync(exitCode: 1);

        var act = () => patcher.PatchAsync(request, Lease(), cached, new JsonObject(), NoLogging);

        // ⚠️ Deliberately NOT PatchNotPossibleException. That one is caught and
        // recovered into a full build; a runner that cannot sign must not be
        // hidden behind builds that merely take longer.
        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Should().NotBeOfType<PatchNotPossibleException>();
    }

    [Fact(DisplayName = "Passwords are passed by file, never as arguments")]
    public async Task PasswordsNeverReachTheCommandLine()
    {
        var (patcher, sandbox, cached) = await ArrangeAsync();

        await patcher.PatchAsync(request, Lease(), cached, new JsonObject(), NoLogging);

        var sign = sandbox.Commands.Single(command => command.Executable == "apksigner");

        // ⚠️ Every process argument on a Linux host is world-readable in /proc,
        // and apksigner echoes its own command line on failure — straight into a
        // build log the customer can download.
        sign.Arguments.Should().NotContain(argument => argument.Contains("hunter2", StringComparison.Ordinal));
        sign.Arguments.Should().Contain("file:" + Path.Combine(root, "store.pw"));
        sign.Arguments.Should().Contain("file:" + Path.Combine(root, "key.pw"));
    }

    [Fact(DisplayName = "zipalign page-aligns, so an APK with native code still runs")]
    public async Task AlignsForNativeCode()
    {
        var (patcher, sandbox, cached) = await ArrangeAsync();

        await patcher.PatchAsync(request, Lease(), cached, new JsonObject(), NoLogging);

        var align = sandbox.Commands.Single(command => command.Executable == "zipalign");

        // Without -p an APK holding native libraries installs and then crashes
        // on devices that map them straight out of the archive.
        align.Arguments.Should().ContainInOrder("-p", "4");
    }

    private static async Task<string> ReadEntryAsync(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path);
        entry.Should().NotBeNull($"the patched APK must still contain {path}");

        using var reader = new StreamReader(await entry!.OpenAsync());
        return await reader.ReadToEndAsync();
    }

    private static Task NoLogging(string line, bool isError, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    private string PatchedPath() => Path.Combine(root, "workspace", "patch", "patched.apk");

    private RunnerLease Lease() =>
        new("lease", "runner", Path.Combine(root, "workspace"), Path.Combine(root, "cache"));

    private async Task<(AndroidContentPatcher Patcher, RecordingSandbox Sandbox, CacheLookup Cached)> ArrangeAsync(
        bool withConfigEntry = true,
        int exitCode = 0)
    {
        Directory.CreateDirectory(root);

        await File.WriteAllTextAsync(Path.Combine(root, "store.pw"), "hunter2-store");
        await File.WriteAllTextAsync(Path.Combine(root, "key.pw"), "hunter2-key");

        var apkPath = Path.Combine(root, "cached.apk");

        using (var file = new FileStream(apkPath, FileMode.Create, FileAccess.Write))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
        {
            Add(archive, "AndroidManifest.xml", "compiled-manifest");
            Add(archive, "classes.dex", "dex-bytes");
            Add(archive, "res/drawable/icon.png", "icon-bytes");
            Add(archive, "META-INF/MANIFEST.MF", "Name: classes.dex\nSHA-256-Digest: old");
            Add(archive, "META-INF/CERT.SF", "signature-file");
            Add(archive, "META-INF/CERT.RSA", "signature-block");

            if (withConfigEntry)
            {
                Add(
                    archive,
                    AndroidContentPatcher.ConfigEntryPath,
                    """{"app":{"initialUrl":"https://before.example"}}""");
            }
        }

        var store = new FileSystemArtifactStore(Options.Create(new ArtifactStorageOptions
        {
            Directory = Path.Combine(root, "store"),
        }));

        var uploaded = await store.StoreAsync(request, apkPath);
        var sandbox = new RecordingSandbox(exitCode);

        var patcher = new AndroidContentPatcher(
            store,
            sandbox,
            new AndroidSigningIdentity(
                Path.Combine(root, "debug.keystore"),
                "androiddebugkey",
                Path.Combine(root, "store.pw"),
                Path.Combine(root, "key.pw")));

        return (patcher, sandbox, new CacheLookup(CacheOutcome.Patch, uploaded.ArtifactReference, uploaded.Bytes));

        static void Add(ZipArchive archive, string path, string content)
        {
            using var writer = new StreamWriter(archive.CreateEntry(path).Open(), Encoding.UTF8);
            writer.Write(content);
        }
    }
}

/// <summary>
/// A sandbox that records commands instead of running them.
/// </summary>
/// <remarks>
/// ⚠️ Recording, not simulating. There is no Android SDK in this environment,
/// so what these tests can honestly assert is that the right tools are invoked
/// with the right arguments — the same level at which the container hardening
/// is asserted, and flagged the same way in the sprint review.
/// </remarks>
/// <param name="exitCode">What every command reports.</param>
internal sealed class RecordingSandbox(int exitCode) : IBuildSandbox
{
    public List<SandboxCommand> Commands { get; } = [];

    public bool IsIsolated => false;

    public Task<RunnerLease> PrepareAsync(
        BuildRequest request,
        RunnerLease lease,
        CancellationToken cancellationToken = default) => Task.FromResult(lease);

    public Task<SandboxResult> RunAsync(
        RunnerLease lease,
        SandboxCommand command,
        LogLineHandler onLine,
        Action? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        Commands.Add(command);
        return Task.FromResult(new SandboxResult(exitCode, TimeSpan.FromMilliseconds(10)));
    }

    public Task DestroyAsync(RunnerLease lease, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
