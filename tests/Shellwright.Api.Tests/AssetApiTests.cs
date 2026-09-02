using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Shellwright.Api.Assets;
using Shellwright.Api.Domain;
using Shellwright.Api.Tests.Infrastructure;
using Xunit;

namespace Shellwright.Api.Tests;

/// <summary>Uploading icons: what the bytes are allowed to be, and what happens twice.</summary>
[Collection(DatabaseFixtureDefinition.Name)]
public sealed class AssetApiTests(PostgresFixture fixture) : IDisposable
{
    private readonly ApiFactory factory = new(fixture);

    /// <inheritdoc />
    public void Dispose() => factory.Dispose();

    /// <summary>A real PNG is accepted and measured from its own bytes.</summary>
    [Fact]
    public async Task A_png_is_accepted_and_measured()
    {
        var tenant = await TenantBuilder.CreateAsync(factory);

        var response = await UploadAsync(tenant, FixturePng(), "image/png");

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var asset = await response.Content.ReadFromJsonAsync<JsonElement>();
        asset.GetProperty("reference").GetString().Should().StartWith("asset://sha256-");
        asset.GetProperty("contentType").GetString().Should().Be("image/png");
        asset.GetProperty("width").GetInt32().Should().Be(1024);
        asset.GetProperty("height").GetInt32().Should().Be(1024);
    }

    /// <summary>
    /// TC-S06-API-035 — the same image twice is one row and one reference.
    /// </summary>
    [Fact]
    public async Task Uploading_the_same_image_twice_deduplicates()
    {
        var tenant = await TenantBuilder.CreateAsync(factory);

        var first = await UploadAsync(tenant, FixturePng(), "image/png");
        var second = await UploadAsync(tenant, FixturePng(), "image/png");

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        var one = await first.Content.ReadFromJsonAsync<JsonElement>();
        var two = await second.Content.ReadFromJsonAsync<JsonElement>();

        two.GetProperty("reference").GetString().Should().Be(one.GetProperty("reference").GetString());
        two.GetProperty("deduplicated").GetBoolean().Should().BeTrue();
    }

    /// <summary>
    /// TC-S06-SEC-007 — a ZIP announced as a PNG is refused.
    /// </summary>
    /// <remarks>
    /// ⚠️ The declared media type is chosen by whoever is uploading. Trusting
    /// it is how an archive, or a file crafted to be read two ways at once,
    /// becomes an app icon.
    /// </remarks>
    [Fact]
    public async Task A_zip_announced_as_a_png_is_refused()
    {
        var tenant = await TenantBuilder.CreateAsync(factory);

        // PK\003\004 — a real ZIP local file header.
        var zip = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00, 0x08, 0x00 };

        var response = await UploadAsync(tenant, zip, "image/png");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("bytes decide");
    }

    /// <summary>A file whose header says PNG and whose body is not one is refused.</summary>
    [Fact]
    public async Task A_truncated_png_is_refused()
    {
        var tenant = await TenantBuilder.CreateAsync(factory);

        var header = FixturePng()[..16];

        var response = await UploadAsync(tenant, header, "image/png");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>An empty upload is refused rather than stored as a zero-byte asset.</summary>
    [Fact]
    public async Task An_empty_upload_is_refused()
    {
        var tenant = await TenantBuilder.CreateAsync(factory);

        var response = await UploadAsync(tenant, [], "image/png");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>Oversized uploads are cut off rather than buffered whole.</summary>
    [Fact]
    public async Task An_oversized_upload_is_refused()
    {
        var tenant = await TenantBuilder.CreateAsync(factory);

        var oversized = new byte[ImageProbe.MaxBytes + 1024];
        FixturePng().AsSpan(0, 8).CopyTo(oversized);

        var response = await UploadAsync(tenant, oversized, "image/png");

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    /// <summary>A viewer may not upload.</summary>
    [Fact]
    public async Task A_viewer_may_not_upload()
    {
        var tenant = await TenantBuilder.CreateAsync(factory);
        var viewer = await TenantBuilder.AddMemberAsync(factory, tenant, OrgRole.Viewer);

        using var content = new ByteArrayContent(FixturePng());
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");

        var response = await viewer.PostAsync(new Uri($"/v1/orgs/{tenant.OrgId}/assets", UriKind.Relative), content);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>An asset uploaded in one organisation is invisible in another.</summary>
    [Fact]
    public async Task Assets_do_not_cross_organisations()
    {
        var mine = await TenantBuilder.CreateAsync(factory);
        var theirs = await TenantBuilder.CreateAsync(factory);

        await UploadAsync(mine, FixturePng(), "image/png");

        var response = await UploadAsync(theirs, FixturePng(), "image/png");

        // ⚠️ Created, not deduplicated. Byte-identical content is one object in
        // the blob store and two rows in the database, because "which
        // organisations hold this image" is not something one tenant may learn
        // about another.
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var asset = await response.Content.ReadFromJsonAsync<JsonElement>();
        asset.GetProperty("deduplicated").GetBoolean().Should().BeFalse();
    }

    /// <summary>An uploaded asset becomes visible to the icon validation rules.</summary>
    [Fact]
    public async Task An_uploaded_asset_satisfies_the_asset_rules()
    {
        var tenant = await TenantBuilder.CreateAsync(factory);

        var uploaded = await UploadAsync(tenant, FixturePng(), "image/png");
        var reference = (await uploaded.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("reference").GetString();

        var current = await tenant.Client.GetFromJsonAsync<JsonElement>(
            new Uri($"/v1/apps/{tenant.AppId}/config", UriKind.Relative));

        var config = current.GetProperty("config").Deserialize<System.Text.Json.Nodes.JsonObject>()!;
        config["branding"] = new System.Text.Json.Nodes.JsonObject
        {
            ["icon"] = reference,
        };

        var response = await tenant.Client.PostAsJsonAsync(
            $"/v1/apps/{tenant.AppId}/config",
            new { config });

        // The rules found the asset, so there is no CFG_ASSET_MISSING. Whatever
        // else the branding block needs, "the file is not there" is not it.
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("CFG_ASSET_MISSING");
    }

    /// <summary>The magic-byte sniffer, on its own.</summary>
    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0 }, "image/png")]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0 }, "image/jpeg")]
    [InlineData(
        new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x45, 0x42, 0x50 },
        "image/webp")]
    public void Recognises(byte[] header, string expected) =>
        ImageProbe.Sniff(header).Should().Be(expected);

    /// <summary>Things that are not images, including ones that start plausibly.</summary>
    [Theory]
    [InlineData(new byte[] { 0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 0x3C, 0x73, 0x76, 0x67, 0, 0, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x41, 0x56, 0x45 })]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0, 0, 0, 0, 0, 0 })]
    public void Refuses(byte[] header) => ImageProbe.Sniff(header).Should().BeNull();

    private static byte[] FixturePng()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Shellwright.slnx")))
        {
            root = root.Parent;
        }

        var path = Path.Combine(
            root!.FullName,
            "tests",
            "fixtures",
            "assets",
            "sha256-ca33913ba4112e9bb80714d79a3c2ece22510c1bbcdefb0030da9e56b59bc2c8.png");

        return File.ReadAllBytes(path);
    }

    private static async Task<HttpResponseMessage> UploadAsync(TenantClient tenant, byte[] bytes, string declared)
    {
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(declared);

        return await tenant.Client.PostAsync(
            new Uri($"/v1/orgs/{tenant.OrgId}/assets", UriKind.Relative),
            content);
    }
}
