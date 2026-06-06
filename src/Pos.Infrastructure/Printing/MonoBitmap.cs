using QRCoder;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Pos.Infrastructure.Printing;

/// <summary>
/// A 1-bit (black/white) bitmap — the common currency for thermal output. Built from ANY client logo
/// image (downscaled + thresholded) or a QR payload (QRCoder module matrix), and packed to the
/// ESC/POS GS v 0 raster format. The preview renderer draws the same bitmap to PNG, so what you see
/// is what prints.
/// </summary>
public sealed class MonoBitmap
{
    public int Width { get; }
    public int Height { get; }
    private readonly bool[] _black; // row-major; true = print (black)

    public MonoBitmap(int width, int height, bool[] black)
    {
        Width = width;
        Height = height;
        _black = black;
    }

    public bool IsBlack(int x, int y) => _black[y * Width + x];

    /// <summary>Downscale any image to <paramref name="maxWidth"/> dots and threshold to 1-bit.</summary>
    public static MonoBitmap FromImage(byte[] image, int maxWidth, byte threshold = 128)
    {
        using var img = Image.Load<Rgba32>(image);
        if (img.Width > maxWidth)
            img.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(maxWidth, 0), Mode = ResizeMode.Max }));

        var w = img.Width;
        var h = img.Height;
        var black = new bool[w * h];
        img.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < h; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < w; x++)
                {
                    var p = row[x];
                    var lum = (int)(0.299 * p.R + 0.587 * p.G + 0.114 * p.B);
                    // Transparent pixels are paper (white); dark opaque pixels print.
                    black[y * w + x] = p.A >= 128 && lum < threshold;
                }
            }
        });
        return new MonoBitmap(w, h, black);
    }

    /// <summary>Rasterize a QR payload to a centered bitmap roughly <paramref name="targetWidth"/> dots wide.</summary>
    public static MonoBitmap FromQr(string data, int targetWidth)
    {
        using var gen = new QRCodeGenerator();
        var qr = gen.CreateQrCode(data ?? "", QRCodeGenerator.ECCLevel.M);
        var matrix = qr.ModuleMatrix;
        var modules = matrix.Count;
        var scale = Math.Clamp(targetWidth / Math.Max(1, modules + 2), 2, 12); // ~module size in dots
        var size = modules * scale;
        var black = new bool[size * size];
        for (var my = 0; my < modules; my++)
            for (var mx = 0; mx < modules; mx++)
                if (matrix[my][mx])
                    for (var dy = 0; dy < scale; dy++)
                        for (var dx = 0; dx < scale; dx++)
                            black[(my * scale + dy) * size + (mx * scale + dx)] = true;
        return new MonoBitmap(size, size, black);
    }

    /// <summary>Pack to ESC/POS raster bit-image: GS v 0 m xL xH yL yH [data].</summary>
    public byte[] ToEscPosRaster()
    {
        var bytesPerRow = (Width + 7) / 8;
        var data = new byte[bytesPerRow * Height];
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
                if (_black[y * Width + x])
                    data[y * bytesPerRow + (x / 8)] |= (byte)(0x80 >> (x % 8)); // MSB = leftmost

        var header = new byte[]
        {
            0x1D, 0x76, 0x30, 0x00,
            (byte)(bytesPerRow & 0xFF), (byte)(bytesPerRow >> 8),
            (byte)(Height & 0xFF), (byte)(Height >> 8),
        };
        var result = new byte[header.Length + data.Length];
        header.CopyTo(result, 0);
        data.CopyTo(result, header.Length);
        return result;
    }
}
