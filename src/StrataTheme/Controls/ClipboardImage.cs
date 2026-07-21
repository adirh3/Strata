using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Media.Imaging;

namespace StrataTheme.Controls;

/// <summary>
/// Cross-platform clipboard image helpers. Avalonia's built-in
/// <see cref="AsyncDataTransferExtensions.TryGetBitmapAsync(IAsyncDataTransfer)"/> only decodes the
/// formats it maps to <see cref="DataFormat.Bitmap"/> (PNG/BMP). That misses macOS, where copied
/// images and screenshots land on the pasteboard as <c>public.tiff</c>, so the built-in path returns
/// null and image paste silently fails. This helper falls back to reading the raw bytes of any
/// image-typed clipboard entry and decoding them with Skia (which handles TIFF/PNG/JPEG/etc.).
/// On Windows/Linux the built-in path succeeds first, so behavior there is unchanged.
/// </summary>
public static class ClipboardImage
{
    // Identifiers that denote raster image payloads across platforms. macOS uses uniform type
    // identifiers (public.*); Windows/Linux/web use MIME types. Matched case-insensitively.
    private static readonly string[] ImageIdentifierHints =
    {
        "public.png", "public.tiff", "public.jpeg", "public.heic", "public.image",
        "image/png", "image/tiff", "image/jpeg", "image/bmp", "image/gif", "image/webp",
    };

    // macOS-only image uniform type identifiers. These never appear on Windows/Linux clipboards, so
    // matching one is an unambiguous "this is a macOS image payload" signal (see HasImageFormat).
    private static readonly string[] MacImageIdentifiers =
    {
        "public.png", "public.tiff", "public.jpeg", "public.heic", "public.image",
    };

    /// <summary>
    /// Attempts to obtain a decoded bitmap from a clipboard payload. Tries Avalonia's built-in bitmap
    /// path first (unchanged behavior on Windows/Linux), then falls back to decoding the raw bytes of
    /// any image-typed format (needed for macOS <c>public.tiff</c>). Returns null when the payload has
    /// no decodable image. The caller owns and must dispose the returned bitmap.
    /// </summary>
    public static async Task<Bitmap?> TryGetImageAsync(
        IAsyncDataTransfer? dataTransfer, Func<byte[], byte[]?>? nativeImageToPng = null)
    {
        if (dataTransfer is null)
            return null;

        try
        {
            var bitmap = await dataTransfer.TryGetBitmapAsync().ConfigureAwait(true);
            if (bitmap is not null)
                return bitmap;
        }
        catch
        {
            // Built-in decode failed for this payload; try the raw-bytes fallback below.
        }

        foreach (var format in dataTransfer.Formats)
        {
            var identifier = format.Identifier;
            if (string.IsNullOrEmpty(identifier) || !IsImageIdentifier(identifier))
                continue;

            byte[]? bytes;
            try
            {
                var bytesFormat = DataFormat.CreateBytesPlatformFormat(identifier);
                bytes = await dataTransfer.TryGetValueAsync(bytesFormat).ConfigureAwait(true);
            }
            catch
            {
                continue;
            }

            if (bytes is not { Length: > 0 })
                continue;

            // Skia (Avalonia's codec) decodes PNG/JPEG/BMP/GIF but NOT TIFF — the format macOS uses for
            // clipboard screenshots. Decode directly when possible; otherwise transcode via the supplied
            // platform decoder (AppKit on macOS) to PNG and decode that.
            try
            {
                return new Bitmap(new MemoryStream(bytes));
            }
            catch
            {
                // Skia couldn't decode these bytes (e.g. TIFF) — fall through to the native transcode.
            }

            if (nativeImageToPng is not null)
            {
                try
                {
                    var png = nativeImageToPng(bytes);
                    if (png is { Length: > 0 })
                        return new Bitmap(new MemoryStream(png));
                }
                catch
                {
                    // Transcode failed; try the next candidate format.
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Whether the payload advertises a macOS image uniform-type-identifier (<c>public.tiff</c> etc.).
    /// These UTIs never appear on Windows/Linux clipboards, so callers can use this as a macOS-only
    /// "an image is present" probe (e.g. to decide to raise a paste event) without affecting other OSes,
    /// and without needing to decode the image — important because TIFF isn't decodable via Skia.
    /// </summary>
    public static bool HasImageFormat(IAsyncDataTransfer? dataTransfer)
    {
        if (dataTransfer is null)
            return false;

        foreach (var format in dataTransfer.Formats)
        {
            var identifier = format.Identifier;
            if (string.IsNullOrEmpty(identifier))
                continue;

            foreach (var uti in MacImageIdentifiers)
            {
                if (identifier.Contains(uti, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private static bool IsImageIdentifier(string identifier)
    {
        foreach (var hint in ImageIdentifierHints)
        {
            if (identifier.Contains(hint, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
