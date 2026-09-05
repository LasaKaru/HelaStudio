using System.Diagnostics.CodeAnalysis;
using SkiaSharp;

namespace Shellwright.Api.Assets;

/// <summary>What an uploaded image turned out to be.</summary>
/// <param name="ContentType">Media type, determined from the bytes.</param>
/// <param name="Width">Pixel width.</param>
/// <param name="Height">Pixel height.</param>
/// <param name="HasAlpha">Whether any pixel is not fully opaque.</param>
public sealed record ProbedImage(string ContentType, int Width, int Height, bool HasAlpha);

/// <summary>
/// Identifies and measures an uploaded image.
/// </summary>
/// <remarks>
/// ⚠️ TC-S06-SEC-007. The declared <c>Content-Type</c> is not consulted at any
/// point. It is chosen by whoever is uploading, and treating it as evidence is
/// how a ZIP becomes an app icon, or how a polyglot file gets served back with
/// a media type that makes a browser execute it. The bytes decide.
///
/// The magic-byte check runs before the decoder rather than relying on the
/// decoder to reject what it cannot read: an image decoder is a large C++
/// attack surface, and the cheapest way to keep hostile input away from it is
/// not to hand it anything that is not already known to be an image.
/// </remarks>
public static class ImageProbe
{
    /// <summary>Largest upload accepted.</summary>
    /// <remarks>
    /// A 1024×1024 PNG is comfortably under a megabyte. Eight leaves room for
    /// a source icon somebody exported carelessly, and stops a single request
    /// from asking the decoder to allocate more than the host has.
    /// </remarks>
    public const int MaxBytes = 8 * 1024 * 1024;

    /// <summary>Largest dimension accepted, in pixels.</summary>
    /// <remarks>
    /// Guards the decode itself: a small compressed file can declare enormous
    /// dimensions, and the allocation happens before anything else can object.
    /// </remarks>
    public const int MaxDimension = 8192;

    /// <summary>Identifies an upload, or explains why it was refused.</summary>
    /// <param name="content">The uploaded bytes.</param>
    /// <param name="image">What it turned out to be.</param>
    /// <returns>Null when accepted, otherwise a message for the caller.</returns>
    [SuppressMessage(
        "Reliability",
        "CA1508:Avoid dead conditional code",
        Justification = "SKCodec.Create is annotated non-nullable and returns null at runtime for data it "
            + "cannot decode. The analyzer believes the annotation; the null check is what stops a "
            + "NullReferenceException on the first truncated upload.")]
    public static string? TryProbe(ReadOnlySpan<byte> content, out ProbedImage? image)
    {
        image = null;

        if (content.Length == 0)
        {
            return "The upload is empty.";
        }

        if (content.Length > MaxBytes)
        {
            return $"Images must be {MaxBytes / (1024 * 1024)} MB or smaller.";
        }

        var contentType = Sniff(content);

        if (contentType is null)
        {
            return "This is not a PNG, JPEG, or WebP. The file's own bytes decide, not its name or its "
                + "declared type.";
        }

        using var data = SKData.CreateCopy(content.ToArray());
        using var codec = SKCodec.Create(data);

        if (codec is null)
        {
            // Recognised header, unreadable body — truncated, or crafted to
            // look like an image.
            return "The file starts like an image but could not be decoded.";
        }

        var info = codec.Info;

        if (info.Width <= 0 || info.Height <= 0)
        {
            return "The image has no dimensions.";
        }

        if (info.Width > MaxDimension || info.Height > MaxDimension)
        {
            return $"Images must be {MaxDimension} pixels or smaller on each side.";
        }

        image = new ProbedImage(contentType, info.Width, info.Height, HasTransparency(codec, info));
        return null;
    }

    /// <summary>Identifies a format from its leading bytes.</summary>
    /// <param name="content">The uploaded bytes.</param>
    /// <returns>The media type, or null when unrecognised.</returns>
    public static string? Sniff(ReadOnlySpan<byte> content) => content switch
    {
        [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, ..] => "image/png",
        [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
        [(byte)'R', (byte)'I', (byte)'F', (byte)'F', _, _, _, _,
         (byte)'W', (byte)'E', (byte)'B', (byte)'P', ..] => "image/webp",
        _ => null,
    };

    /// <summary>
    /// Whether any pixel is not fully opaque.
    /// </summary>
    /// <remarks>
    /// ⚠️ The alpha *channel* is not the question. A PNG saved from most
    /// editors carries one whether or not anything in the image is
    /// transparent, and rejecting on its presence would refuse perfectly good
    /// icons. Apple rejects icons that are actually transparent, so the pixels
    /// are what get inspected.
    /// </remarks>
    private static bool HasTransparency(SKCodec codec, SKImageInfo info)
    {
        if (info.AlphaType == SKAlphaType.Opaque)
        {
            return false;
        }

        using var bitmap = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));

        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
        {
            // Could not read the pixels, so the honest answer is the one that
            // makes the icon rule complain rather than the one that lets a
            // transparent icon through to App Store review.
            return true;
        }

        var pixels = bitmap.GetPixelSpan();
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0xFF)
            {
                return true;
            }
        }

        return false;
    }
}
