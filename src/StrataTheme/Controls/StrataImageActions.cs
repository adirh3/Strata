using System;
using System.Threading;
using System.Threading.Tasks;

namespace StrataTheme.Controls;

/// <summary>How the image viewer's Save keeps a copy of a markdown image.</summary>
public enum StrataImageSaveStyle
{
    /// <summary>A save dialog asks where the copy goes. The viewer does this itself.</summary>
    SaveAs,

    /// <summary>The host downloads the image, as a browser does.</summary>
    Download,

    /// <summary>The host keeps the image with the device's photos.</summary>
    SaveToPhotos,
}

/// <summary>A markdown image the viewer hands to its host to share or save.</summary>
public sealed class StrataImageFile
{
    private readonly Func<CancellationToken, Task<byte[]>> _readBytes;

    /// <summary>Describes an image for a host's share or save.</summary>
    /// <param name="fileName">A file name for the image, with an extension that matches its format.</param>
    /// <param name="localPath">The image's file on this device, when it has one.</param>
    /// <param name="remoteUri">The image's address, when it is not a local file.</param>
    /// <param name="readBytes">Reads the image's bytes.</param>
    public StrataImageFile(
        string fileName,
        string? localPath,
        Uri? remoteUri,
        Func<CancellationToken, Task<byte[]>> readBytes)
    {
        FileName = fileName;
        LocalPath = localPath;
        RemoteUri = remoteUri;
        _readBytes = readBytes;
    }

    /// <summary>A file name for the image, with an extension that matches its format.</summary>
    public string FileName { get; }

    /// <summary>The image's file on this device, when it has one.</summary>
    public string? LocalPath { get; }

    /// <summary>The image's address, when it is not a local file.</summary>
    public Uri? RemoteUri { get; }

    /// <summary>The image's local path, or else its address.</summary>
    public string Location => LocalPath ?? RemoteUri?.OriginalString ?? string.Empty;

    /// <summary>Reads the image as its file or server has it (at most 20 MB).</summary>
    public Task<byte[]> ReadAllBytesAsync(CancellationToken cancellationToken = default) =>
        _readBytes(cancellationToken);
}

/// <summary>
/// What a host adds to the full-screen viewer of markdown images: sharing, saving the platform's way, and
/// whether the image's place on disk means anything to the user. Set one on a container with
/// <see cref="StrataMarkdown.ImageActionHostProperty"/>; every markdown control inside it uses it.
/// </summary>
/// <remarks>
/// Without a host, a desktop offers Copy image, Show in folder, Open in default app and Save a copy…, and
/// a phone or browser offers only Save a copy….
/// </remarks>
public interface IStrataImageActionHost
{
    /// <summary>The platform can hand an image to another app's share sheet.</summary>
    bool CanShare { get; }

    /// <summary>Shares the image. False when it could not be shared; a cancelled share sheet is not a failure.</summary>
    Task<bool> ShareAsync(StrataImageFile image, CancellationToken cancellationToken);

    /// <summary>How Save keeps a copy, which also names the action.</summary>
    StrataImageSaveStyle SaveStyle { get; }

    /// <summary>
    /// Saves the image the way <see cref="SaveStyle"/> says. Not called for
    /// <see cref="StrataImageSaveStyle.SaveAs"/>, which the viewer does itself. False when it could not.
    /// </summary>
    Task<bool> SaveAsync(StrataImageFile image, CancellationToken cancellationToken);

    /// <summary>
    /// The viewer offers the image's file and address: Show in folder, Open in default app and Copy path.
    /// Off where images are private copies the user would not look for.
    /// </summary>
    bool OffersFileLocation { get; }

    /// <summary>The platform's clipboard takes pictures, so the viewer offers Copy image.</summary>
    bool CanCopyImage { get; }
}
