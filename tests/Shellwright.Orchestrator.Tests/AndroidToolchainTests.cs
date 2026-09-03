using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Shellwright.Orchestrator.Artifacts;
using Shellwright.Orchestrator.Patching;
using Shellwright.Orchestrator.Sandbox;
using Shellwright.Orchestrator.Verification;
using Shellwright.Orchestrator.Workflows;
using Xunit;

namespace Shellwright.Orchestrator.Tests;

/// <summary>
/// TC-S07-BLD-094–097 — the patch path against the real Android build tools.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ These run <c>zipalign</c> and <c>apksigner</c> for real, and then ask
/// <c>apksigner verify</c> whether the result is a properly signed APK. Sprint
/// 07 asserted those two steps at the argument level and recorded "no Android
/// SDK in this environment" as a gap — which was simply not checked. The SDK is
/// present, so the gap was mine rather than the environment's.
/// </para>
/// <para>
/// ⚠️ Skipped rather than failed when the tools are absent, because they will
/// be on a machine that has no Android SDK — but the skip is loud in the run
/// output, and <see cref="ToolchainIsPresent"/> records what was looked for and
/// where. A silent skip is how a suite ends up green while testing nothing.
/// </para>
/// </remarks>
public sealed class AndroidToolchainTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"shellwright-toolchain-{Guid.NewGuid():N}");

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

    [SkippableFact(DisplayName = "A patched APK is aligned, signed, and verifies")]
    public async Task PatchedApkVerifies()
    {
        Skip.IfNot(ToolchainIsPresent(out var tools), $"No Android build tools. {tools}");

        var (patcher, sandbox, cached) = await ArrangeAsync();

        var config = new JsonObject
        {
            ["app"] = new JsonObject { ["initialUrl"] = "https://after.example" },
        };

        var built = await patcher.PatchAsync(request, Lease(), cached, config, Log);

        built.WasPatched.Should().BeTrue();
        File.Exists(built.ArtifactPath).Should().BeTrue("zipalign must have written the aligned APK");

        // ⚠️ apksigner's own verdict, not ours. A structural check can say the
        // archive holds a signature block; only the signer can say the
        // signature covers the contents — which is the thing the patch path
        // could plausibly get wrong, because it rewrote those contents.
        var verify = await RunAsync(
            Path.Combine(tools, "apksigner"),
            ["verify", "--verbose", built.ArtifactPath]);

        verify.ExitCode.Should().Be(0, $"apksigner rejected the patched APK:\n{verify.Output}");
        verify.Output.Should().Contain("Verified using v2 scheme");
    }

    [SkippableFact(DisplayName = "The patched APK carries the new configuration, not the old one")]
    public async Task PatchedApkCarriesTheNewConfiguration()
    {
        Skip.IfNot(ToolchainIsPresent(out var tools), $"No Android build tools. {tools}");

        var (patcher, _, cached) = await ArrangeAsync();

        var config = new JsonObject
        {
            ["app"] = new JsonObject { ["initialUrl"] = "https://after.example" },
        };

        var built = await patcher.PatchAsync(request, Lease(), cached, config, Log);

        // Read it back out of the *signed, aligned* artifact rather than the
        // intermediate, so this is the file a device would install.
        using var archive = await ZipFile.OpenReadAsync(built.ArtifactPath);
        var entry = archive.GetEntry(AndroidContentPatcher.ConfigEntryPath);

        entry.Should().NotBeNull();

        using var reader = new StreamReader(await entry!.OpenAsync());
        var written = await reader.ReadToEndAsync();

        written.Should().Contain("after.example");
        written.Should().NotContain("before.example");
    }

    [SkippableFact(DisplayName = "The verifier accepts what the toolchain actually produced")]
    public async Task VerifierAcceptsARealArtifact()
    {
        Skip.IfNot(ToolchainIsPresent(out var tools), $"No Android build tools. {tools}");

        var (patcher, _, cached) = await ArrangeAsync();
        var built = await patcher.PatchAsync(request, Lease(), cached, new JsonObject(), Log);

        var verifier = new AndroidArtifactVerifier(Options.Create(new VerificationOptions
        {
            MaxArtifactBytes = 100 * 1024 * 1024,
            MinArtifactBytes = 1024,
        }));

        // ⚠️ The verifier's rules were written against APKs this repository
        // built by hand. This is the first time they meet one that zipalign and
        // apksigner produced, which is where a rule that was subtly about our
        // own fixtures would show up.
        var verdict = await verifier.VerifyAsync(request, built.ArtifactPath);

        verdict.Accepted.Should().BeTrue(verdict.Reason);
    }

    [SkippableFact(DisplayName = "An unsigned APK is rejected by apksigner and by the verifier alike")]
    public async Task UnsignedApkIsRejected()
    {
        Skip.IfNot(ToolchainIsPresent(out var tools), $"No Android build tools. {tools}");

        var apkPath = await WriteApkAsync(signed: false);

        var verify = await RunAsync(Path.Combine(tools, "apksigner"), ["verify", apkPath]);

        verify.ExitCode.Should().NotBe(0, "an unsigned APK must not verify");

        var verifier = new AndroidArtifactVerifier(Options.Create(new VerificationOptions
        {
            MaxArtifactBytes = 100 * 1024 * 1024,
            MinArtifactBytes = 1024,
        }));

        // Both agree, which is the point: our cheap structural check and the
        // real signer reach the same verdict on the case that matters.
        (await verifier.VerifyAsync(request, apkPath)).Accepted.Should().BeFalse();
    }

    /// <summary>Locates the Android build tools, if they are installed.</summary>
    /// <param name="tools">The directory, or a description of where was searched.</param>
    /// <returns>Whether zipalign and apksigner were both found.</returns>
    private static bool ToolchainIsPresent(out string tools)
    {
        var candidates = new List<string>();

        var home = Environment.GetEnvironmentVariable("ANDROID_HOME")
            ?? Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT")
            ?? "/opt/android-sdk";

        var buildTools = Path.Combine(home, "build-tools");

        if (Directory.Exists(buildTools))
        {
            // Newest first, so a machine with several versions uses the one a
            // build would.
            var versions = Directory.GetDirectories(buildTools);
            Array.Sort(versions, StringComparer.Ordinal);
            Array.Reverse(versions);
            candidates.AddRange(versions);
        }

        foreach (var candidate in candidates)
        {
            if (File.Exists(Path.Combine(candidate, "zipalign"))
                && File.Exists(Path.Combine(candidate, "apksigner")))
            {
                tools = candidate;
                return true;
            }
        }

        tools = $"Looked under {buildTools} (set ANDROID_HOME to point elsewhere).";
        return false;
    }

    private static Task Log(string line, bool isError, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    private static async Task<(int ExitCode, string Output)> RunAsync(
        string executable,
        IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, stdout + stderr);
    }

    private RunnerLease Lease() =>
        new("toolchain", "runner", Path.Combine(root, "workspace"), Path.Combine(root, "cache"));

    private async Task<(AndroidContentPatcher Patcher, IBuildSandbox Sandbox, CacheLookup Cached)> ArrangeAsync()
    {
        Directory.CreateDirectory(root);

        var keystore = await WriteKeystoreAsync();
        var storePassword = Path.Combine(root, "store.pw");
        var keyPassword = Path.Combine(root, "key.pw");

        // ⚠️ No trailing newline. apksigner's `pass:file` reads the first line,
        // and a file written with WriteAllTextAsync plus a newline works — but
        // a file written with WriteAllLinesAsync on Windows would carry a
        // carriage return into the password. Writing the bytes exactly is the
        // habit that avoids a failure nobody can see in a log.
        await File.WriteAllTextAsync(storePassword, "android");
        await File.WriteAllTextAsync(keyPassword, "android");

        var apkPath = await WriteApkAsync(signed: true);

        var store = new FileSystemArtifactStore(Options.Create(new ArtifactStorageOptions
        {
            Directory = Path.Combine(root, "store"),
        }));

        var uploaded = await store.StoreAsync(request, apkPath);

        var sandbox = new LocalBuildSandbox(Options.Create(new SandboxOptions
        {
            // Explicitly allowed: this is our own fixture, on a runner with no
            // container runtime, and the class refuses to construct otherwise.
            AllowUnisolatedSandbox = true,
            WorkspaceRoot = Path.Combine(root, "sandbox"),
            CacheRoot = Path.Combine(root, "sandbox-cache"),
        }));

        ToolchainIsPresent(out var tools);

        var patcher = new AndroidContentPatcher(
            store,
            sandbox,
            new AndroidSigningIdentity(
                keystore,
                "androiddebugkey",
                storePassword,
                keyPassword),
            new AndroidToolchain(tools));

        return (
            patcher,
            sandbox,
            new CacheLookup(CacheOutcome.Patch, uploaded.ArtifactReference, uploaded.Bytes));
    }

    private async Task<string> WriteKeystoreAsync()
    {
        var path = Path.Combine(root, "debug.keystore");

        var result = await RunAsync(
            "keytool",
            [
                "-genkeypair",
                "-keystore", path,
                "-storepass", "android",
                "-keypass", "android",
                "-alias", "androiddebugkey",
                "-keyalg", "RSA",
                "-keysize", "2048",
                "-validity", "10000",

                // ⚠️ The Android debug identity, which is not a secret and is
                // the same on every developer machine. Release signing means
                // holding customers' upload keys and belongs in Sprint 14.
                "-dname", "CN=Android Debug,O=Android,C=US",
            ]);

        result.ExitCode.Should().Be(0, $"keytool failed:\n{result.Output}");

        return path;
    }

    /// <summary>
    /// Builds a real APK with aapt2, then adds what a shell APK carries.
    /// </summary>
    /// <remarks>
    /// ⚠️ Built by the toolchain rather than assembled as a zip by hand, and the
    /// reason is a failure this test hit on the first run: apksigner refuses an
    /// APK whose <c>minSdkVersion</c> it cannot determine, and it reads that
    /// from the <i>compiled binary</i> manifest. A zip with a text file called
    /// AndroidManifest.xml looks like an APK to our own verifier and is not one.
    ///
    /// Passing <c>--min-sdk-version</c> to work around that would have hidden
    /// the difference and tested the patch path against a shape no device would
    /// ever install.
    /// </remarks>
    /// <param name="signed">Whether to sign it, as a cached artifact would be.</param>
    /// <returns>The path to the APK.</returns>
    private async Task<string> WriteApkAsync(bool signed)
    {
        ToolchainIsPresent(out var tools);

        var staging = Directory.CreateDirectory(
            Path.Combine(root, $"staging-{Guid.NewGuid():N}"));

        var manifestPath = Path.Combine(staging.FullName, "AndroidManifest.xml");

        await File.WriteAllTextAsync(
            manifestPath,
            """
            <?xml version="1.0" encoding="utf-8"?>
            <manifest xmlns:android="http://schemas.android.com/apk/res/android"
                package="test.shellwright.fixture">
                <uses-sdk android:minSdkVersion="24" android:targetSdkVersion="36" />
                <application android:label="Fixture" />
            </manifest>
            """);

        var path = Path.Combine(root, $"fixture-{Guid.NewGuid():N}.apk");

        var link = await RunAsync(
            Path.Combine(tools, "aapt2"),
            ["link", "-o", path, "--manifest", manifestPath, "-I", AndroidJar()]);

        link.ExitCode.Should().Be(0, $"aapt2 could not link the fixture:\n{link.Output}");

        // The parts a shell APK carries that aapt2 does not produce: the
        // configuration the app reads at run time, some compiled code, and
        // enough resources that the archive is worth re-compressing.
        await using (var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Update))
        {
            Add(
                archive,
                AndroidContentPatcher.ConfigEntryPath,
                """{"app":{"initialUrl":"https://before.example"}}""");

            Add(archive, "classes.dex", "dex-bytes");

            var noise = new byte[40_000];
            var random = new Random(20260903);

            for (var index = 0; index < 20; index++)
            {
                random.NextBytes(noise);

                await using var writing = await archive
                    .CreateEntry($"res/drawable/asset-{index}.bin", CompressionLevel.Fastest)
                    .OpenAsync();

                await writing.WriteAsync(noise);
            }
        }

        if (!signed)
        {
            return path;
        }

        var keystore = File.Exists(Path.Combine(root, "debug.keystore"))
            ? Path.Combine(root, "debug.keystore")
            : await WriteKeystoreAsync();

        var signResult = await RunAsync(
            Path.Combine(tools, "apksigner"),
            [
                "sign",
                "--ks", keystore,
                "--ks-key-alias", "androiddebugkey",
                "--ks-pass", "pass:android",
                "--key-pass", "pass:android",
                path,
            ]);

        signResult.ExitCode.Should().Be(0, $"apksigner could not sign the fixture:\n{signResult.Output}");

        return path;

        static void Add(ZipArchive archive, string path, string content)
        {
            using var writer = new StreamWriter(archive.CreateEntry(path).Open(), Encoding.UTF8);
            writer.Write(content);
        }
    }

    /// <summary>The platform jar aapt2 links against.</summary>
    /// <returns>Its path.</returns>
    private static string AndroidJar()
    {
        var home = Environment.GetEnvironmentVariable("ANDROID_HOME")
            ?? Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT")
            ?? "/opt/android-sdk";

        var platforms = Path.Combine(home, "platforms");
        var versions = Directory.GetDirectories(platforms);

        Array.Sort(versions, StringComparer.Ordinal);
        Array.Reverse(versions);

        return Path.Combine(versions[0], "android.jar");
    }
}
