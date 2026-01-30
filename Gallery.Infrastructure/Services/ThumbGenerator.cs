using Gallery.Application.Interfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace Gallery.Infrastructure.Services;

/// <summary>
/// Generates JPEG thumbnails using ImageSharp.
/// </summary>
public sealed class ThumbGenerator : IThumbGenerator
{
    private static readonly JpegEncoder JpegEncoder = new()
    {
        Quality = 82
    };

    public IReadOnlySet<string> SupportedImageExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".tif"
    };

    public async Task<byte[]> GenerateImageThumbAsync(string sourcePath, int maxPixels, CancellationToken ct = default)
    {
        using var image = await Image.LoadAsync(sourcePath, ct);

        // Auto-rotate based on EXIF orientation
        image.Mutate(x => x.AutoOrient());

        // Resize preserving aspect ratio
        if (image.Width > maxPixels || image.Height > maxPixels)
        {
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(maxPixels, maxPixels),
                Mode = ResizeMode.Max
            }));
        }

        using var ms = new MemoryStream();
        await image.SaveAsJpegAsync(ms, JpegEncoder, ct);
        return ms.ToArray();
    }
}
