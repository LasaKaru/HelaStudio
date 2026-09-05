using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Shellwright.Orchestrator.Fleet;
using Shellwright.Orchestrator.Verification;
using Shellwright.Orchestrator.Workflows;
using Xunit;

namespace Shellwright.Orchestrator.Tests;

/// <summary>
/// TC-S08-BLD-013–028 — the iOS build commands and what an IPA must contain.
/// </summary>
/// <remarks>
/// ⚠️ Argument-level for the commands, because there is no macOS here and
/// there will not be one until a Mac exists. That is the same footing as the
/// container hardening, and it is recorded as a gap rather than presented as a
/// working iOS pipeline. The verifier, by contrast, is tested against real
/// archives — it reads bytes, and bytes are the same on any platform.
/// </remarks>
public sealed class IosBuildTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"shellwright-ios-{Guid.NewGuid():N}");

    private readonly BuildRequest request = new(
        BuildId: Guid.NewGuid(),
        OrgId: Guid.NewGuid(),
        AppId: Guid.NewGuid(),
        ConfigVersionId: Guid.NewGuid(),
        Platform: BuildPlatform.Ios,
        Type: BuildType.Release);

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact(DisplayName = "The Xcode version is selected per build, not taken from the host")]
    public void SelectsXcodeExplicitly()
    {
        var toolchain = new XcodeToolchain("26.1", "/Applications/Xcode-26.1.app/Contents/Developer");

        var archive = IosBuildCommands.Archive(
            toolchain,
            "acme",
            "/w/project",
            "/w/out.xcarchive",
            "/w/derived",
            BuildType.Release);

        // ⚠️ DEVELOPER_DIR, not `xcode-select --switch`. The switch is
        // machine-global and would change which Xcode every other build on the
        // host is using, mid-flight.
        archive.Environment!["DEVELOPER_DIR"]
            .Should().Be("/Applications/Xcode-26.1.app/Contents/Developer");
    }

    [Fact(DisplayName = "The toolchain identity is part of what a build is cached under")]
    public void ToolchainIdentityIsStable()
    {
        // Two builds of the same configuration under different Xcodes are
        // different binaries. Treating them as interchangeable would serve a
        // customer an artifact built by a toolchain they did not ask for.
        new XcodeToolchain("26.1", null).ToolchainIdentity()
            .Should().NotBe(new XcodeToolchain("26.0", null).ToolchainIdentity());

        new XcodeToolchain("26.1", "/a").ToolchainIdentity()
            .Should().Be(
                new XcodeToolchain("26.1", "/b").ToolchainIdentity(),
                "where Xcode is installed is a property of the host, not of the build");
    }

    [Fact(DisplayName = "A build never asks Apple to mint credentials on the customer's team")]
    public void ProvisioningUpdatesAreRefused()
    {
        var toolchain = new XcodeToolchain("26.1", null);

        var archive = IosBuildCommands.Archive(
            toolchain, "acme", "/w", "/w/a.xcarchive", "/w/d", BuildType.Release);

        var export = IosBuildCommands.Export(toolchain, "/w/a.xcarchive", "/w/opts.plist", "/w/out", "/w");

        // ⚠️ xcodebuild will otherwise create certificates and profiles on the
        // customer's Apple team as a side effect of compiling. Signing is a
        // deliberate, audited step with custody rules, not something a build
        // does on its own initiative.
        archive.Arguments.Should().ContainInOrder("-allowProvisioningUpdates", "NO");
        export.Arguments.Should().ContainInOrder("-allowProvisioningUpdates", "NO");
    }

    [Fact(DisplayName = "Export options ask for manual signing")]
    public void ExportOptionsUseManualSigning()
    {
        var plist = IosBuildCommands.ExportOptions(IosExportMethod.AppStore, "ABCDE12345");

        plist.Should().Contain("<key>signingStyle</key>");
        plist.Should().Contain("manual");
        plist.Should().Contain("app-store-connect");
        plist.Should().Contain("ABCDE12345");
    }

    [Theory(DisplayName = "Each export method names the value Apple expects")]
    [InlineData(IosExportMethod.Development, "development")]
    [InlineData(IosExportMethod.AdHoc, "ad-hoc")]
    [InlineData(IosExportMethod.AppStore, "app-store-connect")]
    public void ExportMethodsAreNamedCorrectly(IosExportMethod method, string expected)
    {
        // A wrong value here produces an export that fails after the archive —
        // the slowest possible place to discover a typo.
        IosBuildCommands.ExportOptions(method, "ABCDE12345").Should().Contain($"<string>{expected}</string>");
    }

    [Fact(DisplayName = "Every command is an argument array, never a shell string")]
    public void CommandsAreArgumentArrays()
    {
        // ⚠️ A scheme name derives from a customer's app name, and
        // `Foo"; rm -rf ~ #` is a legal app name. On macOS this matters more
        // rather than less: the build runs on a machine holding signing keys.
        var hostile = """Foo"; rm -rf ~ #""";

        var archive = IosBuildCommands.Archive(
            new XcodeToolchain("26.1", null), hostile, "/w", "/w/a.xcarchive", "/w/d", BuildType.Debug);

        archive.Arguments.Should().Contain(hostile, "the name must survive as one argument");
        archive.Executable.Should().Be("xcodebuild");
    }

    [Fact(DisplayName = "A well-formed IPA is accepted")]
    public async Task AcceptsAGoodIpa()
    {
        var ipa = await WriteIpaAsync();

        var verdict = await Verifier().VerifyAsync(request, ipa);

        verdict.Accepted.Should().BeTrue(verdict.Reason);
    }

    [Fact(DisplayName = "An IPA with no app bundle is rejected")]
    public async Task RejectsAnIpaWithNoApp()
    {
        var verdict = await Verifier().VerifyAsync(request, await WriteIpaAsync(withApp: false));

        verdict.Accepted.Should().BeFalse();
        verdict.Reason.Should().Contain("Payload/*.app");
    }

    [Fact(DisplayName = "An IPA with no embedded provisioning profile is rejected")]
    public async Task RejectsAnIpaWithNoProfile()
    {
        var verdict = await Verifier().VerifyAsync(request, await WriteIpaAsync(withProfile: false));

        // ⚠️ The failure that only appears on somebody else's phone: it
        // installs from Xcode on the build machine and nowhere else.
        verdict.Accepted.Should().BeFalse();
        verdict.Reason.Should().Contain("embedded.mobileprovision");
    }

    [Fact(DisplayName = "An IPA with no Info.plist is rejected")]
    public async Task RejectsAnIpaWithNoInfoPlist()
    {
        var verdict = await Verifier().VerifyAsync(request, await WriteIpaAsync(withInfoPlist: false));

        verdict.Accepted.Should().BeFalse();
        verdict.Reason.Should().Contain("Info.plist");
    }

    [Fact(DisplayName = "An IPA with no executable is rejected")]
    public async Task RejectsAnIpaWithNoExecutable()
    {
        var verdict = await Verifier().VerifyAsync(request, await WriteIpaAsync(withExecutable: false));

        verdict.Accepted.Should().BeFalse();
        verdict.Reason.Should().Contain("no executable");
    }

    [Fact(DisplayName = "An IPA carrying two app bundles is rejected")]
    public async Task RejectsTwoAppBundles()
    {
        var verdict = await Verifier().VerifyAsync(request, await WriteIpaAsync(secondApp: true));

        // Accepted by the archive format, rejected by App Store Connect after
        // the customer has waited for the upload.
        verdict.Accepted.Should().BeFalse();
        verdict.Reason.Should().Contain("more than one app bundle");
    }

    [Fact(DisplayName = "The iOS verifier refuses an Android request rather than passing it")]
    public async Task RefusesAnotherPlatform()
    {
        var verdict = await Verifier().VerifyAsync(
            request with { Platform = BuildPlatform.Android },
            await WriteIpaAsync());

        verdict.Accepted.Should().BeFalse();
    }

    [Fact(DisplayName = "An IPA is found inside the directory xcodebuild exports to")]
    public async Task FindsTheIpaInsideAnExportDirectory()
    {
        var ipa = await WriteIpaAsync();
        var exportDirectory = Path.Combine(root, "export");
        Directory.CreateDirectory(exportDirectory);
        File.Move(ipa, Path.Combine(exportDirectory, "Acme.ipa"));

        (await Verifier().VerifyAsync(request, exportDirectory)).Accepted.Should().BeTrue();
    }

    private static IosArtifactVerifier Verifier() =>
        new(Options.Create(new VerificationOptions
        {
            MaxArtifactBytes = 100 * 1024 * 1024,
            MinArtifactBytes = 1024,
        }));

    private async Task<string> WriteIpaAsync(
        bool withApp = true,
        bool withInfoPlist = true,
        bool withProfile = true,
        bool withExecutable = true,
        bool secondApp = false)
    {
        Directory.CreateDirectory(root);

        var path = Path.Combine(root, $"fixture-{Guid.NewGuid():N}.ipa");

        await using (var file = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
        {
            if (withApp)
            {
                if (withInfoPlist)
                {
                    Add(archive, "Payload/Acme.app/Info.plist", "<plist/>");
                }

                if (withProfile)
                {
                    Add(archive, "Payload/Acme.app/embedded.mobileprovision", "profile-bytes");
                }

                if (withExecutable)
                {
                    // No extension, at the bundle root — the shape of a Mach-O.
                    Add(archive, "Payload/Acme.app/Acme", "mach-o-bytes");
                }

                Add(archive, "Payload/Acme.app/Assets.car", "compiled-assets");
            }

            if (secondApp)
            {
                Add(archive, "Payload/Other.app/Info.plist", "<plist/>");
                Add(archive, "Payload/Other.app/Other", "mach-o-bytes");
            }

            // Enough incompressible padding that the size floor is cleared.
            var noise = new byte[8_000];
            new Random(20260903).NextBytes(noise);

            await using var writing = await archive.CreateEntry("Symbols/pad.bin").OpenAsync();
            await writing.WriteAsync(noise);
        }

        return path;

        static void Add(ZipArchive archive, string path, string content)
        {
            using var writer = new StreamWriter(archive.CreateEntry(path).Open(), Encoding.UTF8);
            writer.Write(content);
        }
    }
}
