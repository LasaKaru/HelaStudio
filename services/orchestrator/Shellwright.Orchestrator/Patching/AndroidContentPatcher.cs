using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Shellwright.ConfigSchema;
using Shellwright.Orchestrator.Activities;
using Shellwright.Orchestrator.Sandbox;
using Shellwright.Orchestrator.Workflows;

namespace Shellwright.Orchestrator.Patching;

/// <summary>
/// Turns a cached APK into a new one by replacing the configuration it reads at
/// run time, then re-aligning and re-signing it.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ This is the fast path the three-way cache key exists for. When only the
/// content key has moved — a start URL, an allowed origin, a version string,
/// the navigation structure — nothing compiled has changed, and the entire
/// difference between the previous APK and the requested one is the bytes of
/// <c>assets/appconfig.json</c>. Replacing that file takes about as long as
/// copying the APK; compiling it again takes minutes of metered runner time.
/// </para>
/// <para>
/// ⚠️ Entries are streamed from the old archive into a new one rather than
/// edited in place. <see cref="ZipArchiveMode.Update"/> materialises the whole
/// archive in memory, and a release APK is tens of megabytes with several
/// builds running at once.
/// </para>
/// <para>
/// ⚠️ The old signature is dropped, so that the intermediate archive is
/// honestly unsigned until <c>apksigner</c> has run. That matters on the
/// failure path: if signing fails the aligned file is still on disk, and it
/// must not look like a signed APK to anything that finds it.
/// </para>
/// <para>
/// It is <i>not</i> what makes the output valid. Tested against the real tool:
/// apksigner replaces <c>META-INF</c> wholesale when it signs, so leaving the
/// stale entries in place still verifies — under v1 signing as well as v2 and
/// v3. An earlier version of this comment claimed otherwise and was wrong.
/// </para>
/// </remarks>
/// <param name="artifacts">Where cached artifacts are fetched from.</param>
/// <param name="sandbox">Runs the align and sign steps.</param>
/// <param name="signing">Which key to sign with.</param>
/// <param name="toolchain">Which build tools to run.</param>
public sealed class AndroidContentPatcher(
    IArtifactStore artifacts,
    IBuildSandbox sandbox,
    AndroidSigningIdentity signing,
    AndroidToolchain toolchain) : IArtifactPatcher
{
    /// <summary>Where the shell reads its configuration from inside the APK.</summary>
    /// <remarks>
    /// ⚠️ Must match <c>AndroidProjectGenerator.ConfigAssetPath</c> with its
    /// <c>app/src/main/</c> source-set prefix removed — that is the path the
    /// packaged APK ends up with. A patcher writing to the wrong path produces
    /// an APK that installs, runs, and silently shows the previous
    /// configuration, which no test of the patch mechanics would catch.
    /// </remarks>
    public const string ConfigEntryPath = "assets/appconfig.json";

    private static readonly string[] SignatureExtensions = [".SF", ".RSA", ".DSA", ".EC"];

    /// <inheritdoc />
    public bool Supports(BuildPlatform platform) => platform == BuildPlatform.Android;

    /// <inheritdoc />
    public async Task<BuiltArtifact> PatchAsync(
        BuildRequest request,
        RunnerLease lease,
        CacheLookup cached,
        JsonObject resolvedConfig,
        LogLineHandler onLine,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(cached);
        ArgumentNullException.ThrowIfNull(resolvedConfig);
        ArgumentNullException.ThrowIfNull(onLine);

        if (!Supports(request.Platform))
        {
            throw new PatchNotPossibleException(
                $"{request.Platform} artifacts are not patched by this patcher.");
        }

        if (cached.ArtifactReference is null)
        {
            throw new PatchNotPossibleException(
                "The cache reported a patchable hit without an artifact to patch.");
        }

        var start = DateTimeOffset.UtcNow;
        var workspace = Directory.CreateDirectory(Path.Combine(lease.WorkspaceRoot, "patch"));

        var basePath = Path.Combine(workspace.FullName, "base.apk");
        var patchedPath = Path.Combine(workspace.FullName, "patched.apk");
        var alignedPath = Path.Combine(workspace.FullName, "aligned.apk");

        await onLine("Reusing the previous build: only the app's content changed.", false, cancellationToken);

        var bytes = await artifacts.FetchAsync(cached.ArtifactReference, basePath, cancellationToken);

        await onLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Fetched the cached artifact ({bytes:N0} bytes)."),
            false,
            cancellationToken);

        ReplaceConfig(basePath, patchedPath, resolvedConfig, cancellationToken);

        await onLine("Replaced the app configuration and dropped the old signature.", false, cancellationToken);

        await RunAsync(
            lease,
            AndroidPatchCommands.Align(toolchain, patchedPath, alignedPath),
            "zipalign",
            onLine,
            cancellationToken);

        await RunAsync(
            lease,
            AndroidPatchCommands.Sign(toolchain, signing, alignedPath),
            "apksigner",
            onLine,
            cancellationToken);

        var elapsed = DateTimeOffset.UtcNow - start;

        await onLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Patched build finished in {elapsed.TotalSeconds:F1}s without running a compiler."),
            false,
            cancellationToken);

        return new BuiltArtifact(
            alignedPath,

            // ⚠️ Rounded up, so a patch that took 400 ms is metered as one
            // second rather than zero. Free work is a rounding decision nobody
            // revisits, and at scale it is the difference between the fast path
            // paying for itself and quietly subsidising every account.
            (int)Math.Ceiling(elapsed.TotalSeconds),
            WasPatched: true);
    }

    private static void ReplaceConfig(
        string sourcePath,
        string destinationPath,
        JsonObject resolvedConfig,
        CancellationToken cancellationToken)
    {
        // ⚠️ Canonical bytes, the same serialisation the cache key was computed
        // over. Writing the configuration any other way would put an APK in the
        // cache under a content key its own contents do not produce, and the
        // next identical request would miss.
        var config = CanonicalJson.SerializeToUtf8(resolvedConfig);

        using var source = ZipFile.OpenRead(sourcePath);

        if (source.GetEntry(ConfigEntryPath) is null)
        {
            throw new PatchNotPossibleException(
                $"The cached artifact has no {ConfigEntryPath}. It was not built by a shell "
                + "that reads its configuration at run time, so its content cannot be replaced.");
        }

        using var destinationFile = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);

        using var destination = new ZipArchive(destinationFile, ZipArchiveMode.Create);

        foreach (var entry in source.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entry.FullName == ConfigEntryPath || IsSignature(entry.FullName))
            {
                continue;
            }

            var copy = destination.CreateEntry(entry.FullName, CompressionLevel.Optimal);
            copy.LastWriteTime = entry.LastWriteTime;

            using var reading = entry.Open();
            using var writing = copy.Open();
            reading.CopyTo(writing);
        }

        var replacement = destination.CreateEntry(ConfigEntryPath, CompressionLevel.Optimal);
        using var writer = replacement.Open();
        writer.Write(config);
    }

    private static bool IsSignature(string entryPath)
    {
        if (!entryPath.StartsWith("META-INF/", StringComparison.Ordinal))
        {
            return false;
        }

        // The v1 manifest goes too, not only the signature blocks: it lists a
        // digest for every entry, including the one just replaced, so it is
        // stale the moment the config is swapped. apksigner regenerates it, but
        // an archive that has not reached the signer yet should not carry
        // digests that disagree with its own contents.
        if (entryPath.Equals("META-INF/MANIFEST.MF", StringComparison.Ordinal))
        {
            return true;
        }

        foreach (var extension in SignatureExtensions)
        {
            if (entryPath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task RunAsync(
        RunnerLease lease,
        SandboxCommand command,
        string tool,
        LogLineHandler onLine,
        CancellationToken cancellationToken)
    {
        var result = await sandbox.RunAsync(lease, command, onLine, onProgress: null, cancellationToken);

        if (result.ExitCode != 0)
        {
            // ⚠️ Not a PatchNotPossibleException. A cached artifact that cannot
            // be patched is an expected condition the caller recovers from; a
            // signing tool that fails is a broken runner, and quietly falling
            // back to a full build would hide a fleet that cannot sign anything.
            throw new InvalidOperationException(
                $"{tool} exited with code {result.ExitCode} while patching. See the build log.");
        }
    }
}

/// <summary>How a patched APK is signed.</summary>
/// <param name="KeystorePath">Where the keystore is on the runner.</param>
/// <param name="KeyAlias">Which key inside it.</param>
/// <param name="StorePasswordFile">
/// A file holding the store password.
/// </param>
/// <param name="KeyPasswordFile">A file holding the key password.</param>
/// <remarks>
/// <para>
/// ⚠️ Passwords by file, never on the command line. Every argument of every
/// process on a Linux host is world-readable in <c>/proc</c>, and Gradle and
/// apksigner both echo their own command line on failure — which is exactly
/// how a keystore password reaches a build log a customer can download.
/// <c>apksigner</c> supports <c>pass:file</c> for this reason.
/// </para>
/// <para>
/// ⚠️ In Sprint 07 this is the Android debug key, which is not a secret and is
/// the same on every machine. Release signing means holding customers' upload
/// keys, which needs the custody design in §18.2 of the specification and is
/// tracked in <c>ACTION_REQUIRED.md</c> — it is not something to arrive at by
/// accident because a code path happened to work.
/// </para>
/// </remarks>
public sealed record AndroidSigningIdentity(
    string KeystorePath,
    string KeyAlias,
    string StorePasswordFile,
    string KeyPasswordFile);

/// <summary>
/// Which Android build tools to run.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ An explicit location rather than whatever is on <c>PATH</c>. Bare tool
/// names take the first <c>zipalign</c> the process happens to find, which
/// makes the toolchain an accident of how the runner was provisioned — and the
/// cache key claims a toolchain identity it would then not control. Pinning a
/// build-tools version per app (BD-09) is impossible until the command names a
/// directory.
/// </para>
/// <para>
/// Null falls back to the bare names, which is right inside the runner image
/// where the tools are on <c>PATH</c> by construction and there is exactly one
/// version installed.
/// </para>
/// </remarks>
/// <param name="BuildToolsPath">
/// The <c>build-tools/&lt;version&gt;</c> directory, or null to use <c>PATH</c>.
/// </param>
public sealed record AndroidToolchain(string? BuildToolsPath)
{
    /// <summary>The toolchain the runner image provides on its path.</summary>
    public static AndroidToolchain FromPath { get; } = new((string?)null);

    /// <summary>Resolves one tool to something a process can start.</summary>
    /// <param name="tool">The tool's name, such as <c>zipalign</c>.</param>
    /// <returns>An absolute path, or the bare name.</returns>
    public string Resolve(string tool) =>
        string.IsNullOrWhiteSpace(BuildToolsPath) ? tool : Path.Combine(BuildToolsPath, tool);
}

/// <summary>The commands the patch path runs.</summary>
/// <remarks>
/// ⚠️ Argument arrays, for the same reason as <see cref="BuildCommands"/>: file
/// names on this path derive from a customer's app, and a shell string is a
/// remote code execution with a REST endpoint in front of it.
/// </remarks>
public static class AndroidPatchCommands
{
    /// <summary>Re-aligns a rebuilt archive.</summary>
    /// <param name="toolchain">Which build tools to run.</param>
    /// <param name="input">The rebuilt APK.</param>
    /// <param name="output">Where to write the aligned one.</param>
    /// <returns>The command.</returns>
    public static SandboxCommand Align(AndroidToolchain toolchain, string input, string output) =>
        new(
            (toolchain ?? AndroidToolchain.FromPath).Resolve("zipalign"),
            [
                // Overwrite: the workspace is fresh, but a retried activity
                // reruns this step and must not fail on its own leftovers.
                "-f",

                // ⚠️ Page-align the shared libraries. Without -p an APK with
                // native code installs and then crashes on devices that mmap
                // libraries straight out of the archive.
                "-p",
                "4",
                input,
                output,
            ],
            Path.GetDirectoryName(output) ?? ".");

    /// <summary>Signs an aligned archive.</summary>
    /// <param name="toolchain">Which build tools to run.</param>
    /// <param name="signing">Which key to sign with.</param>
    /// <param name="apkPath">The aligned APK, signed in place.</param>
    /// <returns>The command.</returns>
    public static SandboxCommand Sign(
        AndroidToolchain toolchain,
        AndroidSigningIdentity signing,
        string apkPath)
    {
        ArgumentNullException.ThrowIfNull(signing);

        return new SandboxCommand(
            (toolchain ?? AndroidToolchain.FromPath).Resolve("apksigner"),
            [
                "sign",
                "--ks",
                signing.KeystorePath,
                "--ks-key-alias",
                signing.KeyAlias,
                "--ks-pass",
                $"file:{signing.StorePasswordFile}",
                "--key-pass",
                $"file:{signing.KeyPasswordFile}",

                // v2 and v3 sign the archive as a whole rather than entry by
                // entry, which is what makes the replaced entry above safe.
                "--v2-signing-enabled",
                "true",
                "--v3-signing-enabled",
                "true",
                apkPath,
            ],
            Path.GetDirectoryName(apkPath) ?? ".");
    }
}
