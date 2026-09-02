using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Shellwright.Orchestrator.Patching;
using Shellwright.Orchestrator.Verification;
using Shellwright.Orchestrator.Workflows;
using Xunit;

namespace Shellwright.Orchestrator.Tests;

/// <summary>
/// TC-S07-BLD-065–075 — nothing unusable reaches a customer.
/// </summary>
/// <remarks>
/// ⚠️ Every rejection here corresponds to a way a build exits zero and produces
/// something broken. Gradle exits zero having assembled nothing; a packaging
/// step drops the assets directory and the app launches blank; a patch loses
/// its signature and the APK cannot be installed at all. A verifier written
/// against imagined failures would pass all of these.
/// </remarks>
public sealed class AndroidArtifactVerifierTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"shellwright-verify-{Guid.NewGuid():N}");

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

    [Fact(DisplayName = "A well-formed APK is accepted")]
    public async Task AcceptsAGoodApk()
    {
        var apk = await WriteApkAsync();

        var verdict = await Verifier().VerifyAsync(request, apk);

        verdict.Accepted.Should().BeTrue(verdict.Reason);
    }

    [Fact(DisplayName = "An APK is found inside the directory Gradle reports")]
    public async Task FindsTheApkInsideAnOutputDirectory()
    {
        var apk = await WriteApkAsync();
        var outputs = Path.Combine(root, "outputs");
        Directory.CreateDirectory(outputs);
        File.Move(apk, Path.Combine(outputs, "app-debug.apk"));

        var verdict = await Verifier().VerifyAsync(request, outputs);

        verdict.Accepted.Should().BeTrue(verdict.Reason);
    }

    [Fact(DisplayName = "A build that produced nothing is rejected, not accepted by default")]
    public async Task RejectsAMissingArtifact()
    {
        var verdict = await Verifier().VerifyAsync(request, Path.Combine(root, "nothing.apk"));

        verdict.Accepted.Should().BeFalse();
        verdict.Reason.Should().Contain("no APK");
    }

    [Fact(DisplayName = "A green build that assembled a stub is rejected")]
    public async Task RejectsAnImplausiblySmallApk()
    {
        var apk = await WriteApkAsync(padding: 0);

        var verdict = await Verifier().VerifyAsync(request, apk);

        verdict.Accepted.Should().BeFalse();
        verdict.Reason.Should().Contain("too small");
    }

    [Fact(DisplayName = "An APK over Play's limit is rejected here rather than by the Play Console")]
    public async Task RejectsAnOversizeApk()
    {
        var apk = await WriteApkAsync(padding: 400_000);

        var verdict = await Verifier(maxBytes: 200_000).VerifyAsync(request, apk);

        verdict.Accepted.Should().BeFalse();
        verdict.Reason.Should().Contain("Google Play");
    }

    [Fact(DisplayName = "A file that is not an archive is rejected")]
    public async Task RejectsACorruptArchive()
    {
        Directory.CreateDirectory(root);
        var apk = Path.Combine(root, "corrupt.apk");
        await File.WriteAllBytesAsync(apk, new byte[300_000]);

        var verdict = await Verifier().VerifyAsync(request, apk);

        verdict.Accepted.Should().BeFalse();
        verdict.Reason.Should().Contain("not a readable archive");
    }

    [Fact(DisplayName = "A zip that is not an Android package is rejected")]
    public async Task RejectsAZipWithNoManifest()
    {
        var apk = await WriteApkAsync(withManifest: false);

        var verdict = await Verifier().VerifyAsync(request, apk);

        verdict.Accepted.Should().BeFalse();
        verdict.Reason.Should().Contain("AndroidManifest.xml");
    }

    [Fact(DisplayName = "An APK with no compiled code is rejected")]
    public async Task RejectsAnApkWithNoDex()
    {
        var apk = await WriteApkAsync(withDex: false);

        var verdict = await Verifier().VerifyAsync(request, apk);

        verdict.Accepted.Should().BeFalse();
        verdict.Reason.Should().Contain("no compiled code");
    }

    [Fact(DisplayName = "An APK with no configuration is rejected, because it would launch blank")]
    public async Task RejectsAnApkWithNoConfig()
    {
        var apk = await WriteApkAsync(withConfig: false);

        var verdict = await Verifier().VerifyAsync(request, apk);

        // ⚠️ This is the failure that gets blamed on the customer's website. A
        // crash gets reported; an app that installs, launches, and shows
        // nothing looks like their problem.
        verdict.Accepted.Should().BeFalse();
        verdict.Reason.Should().Contain(AndroidContentPatcher.ConfigEntryPath);
    }

    [Fact(DisplayName = "An unsigned APK is rejected")]
    public async Task RejectsAnUnsignedApk()
    {
        var apk = await WriteApkAsync(withSignature: false);

        var verdict = await Verifier().VerifyAsync(request, apk);

        verdict.Accepted.Should().BeFalse();
        verdict.Reason.Should().Contain("no signature");
    }

    [Fact(DisplayName = "A platform this verifier cannot inspect is rejected, not waved through")]
    public async Task RejectsAPlatformItCannotCheck()
    {
        var apk = await WriteApkAsync();

        var verdict = await Verifier().VerifyAsync(request with { Platform = BuildPlatform.Ios }, apk);

        // ⚠️ A verifier that returns "fine" for what it cannot inspect is worse
        // than none at all: the whole point is that nothing ships unchecked.
        verdict.Accepted.Should().BeFalse();
    }

    private static AndroidArtifactVerifier Verifier(long maxBytes = 100 * 1024 * 1024) =>
        new(Options.Create(new VerificationOptions
        {
            MaxArtifactBytes = maxBytes,
            MinArtifactBytes = 200 * 1024,
        }));

    private async Task<string> WriteApkAsync(
        bool withManifest = true,
        bool withDex = true,
        bool withConfig = true,
        bool withSignature = true,
        int padding = 400_000)
    {
        Directory.CreateDirectory(root);

        var path = Path.Combine(root, $"app-{Guid.NewGuid():N}.apk");

        await using (var file = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
        {
            if (withManifest)
            {
                Add(archive, "AndroidManifest.xml", "compiled-manifest");
            }

            if (withDex)
            {
                Add(archive, "classes.dex", "dex-bytes");
            }

            if (withConfig)
            {
                Add(archive, AndroidContentPatcher.ConfigEntryPath, """{"app":{}}""");
            }

            if (withSignature)
            {
                Add(archive, "META-INF/CERT.RSA", "signature-block");
            }

            if (padding > 0)
            {
                // ⚠️ Incompressible, so the size checks see the size intended.
                // A padding of zero bytes compresses to nothing and would make
                // every "large enough" APK here fail the minimum instead.
                var noise = new byte[padding];
                var random = new Random(20260902);
                random.NextBytes(noise);

                await using var writing = await archive.CreateEntry("lib/arm64-v8a/libshell.so").OpenAsync();
                await writing.WriteAsync(noise);
            }
        }

        return path;

        static void Add(ZipArchive archive, string path, string content)
        {
            using var writer = new StreamWriter(archive.CreateEntry(path).Open(), Encoding.UTF8);
            writer.Write(content);
        }
    }
}
