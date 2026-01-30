namespace Gallery.Application.Interfaces;

/// <summary>
/// Generates thumbnail images from source files.
/// </summary>
public interface IThumbGenerator
{
    /// <summary>
    /// Generate a JPEG thumbnail from a source image.
    /// </summary>
    /// <param name="sourcePath">Path to source image</param>
    /// <param name="maxPixels">Maximum dimension (width or height)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>JPEG bytes</returns>
    Task<byte[]> GenerateImageThumbAsync(string sourcePath, int maxPixels, CancellationToken ct = default);

    /// <summary>
    /// Supported image extensions (lowercase, with dot).
    /// </summary>
    IReadOnlySet<string> SupportedImageExtensions { get; }
}
