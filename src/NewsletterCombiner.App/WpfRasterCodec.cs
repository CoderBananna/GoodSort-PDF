using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NewsletterCombiner.Core;

namespace NewsletterCombiner.App;

public sealed class WpfRasterCodec : IRasterCodec
{
    public EncodedRaster? Encode(RasterInput image, CompressionProfile profile)
    {
        try
        {
            BitmapSource bitmap;
            using var input = new MemoryStream(image.Data, writable: false);
            if (image.IsJpeg)
            {
                bitmap = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                if (bitmap.PixelWidth != image.Width || bitmap.PixelHeight != image.Height) return null;
            }
            else bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96,
                image.IsGrayscale ? PixelFormats.Gray8 : PixelFormats.Rgb24, null, image.Data, image.Width * (image.IsGrayscale ? 1 : 3));
            bitmap = Resize(bitmap, profile.LongEdge);
            return new(EncodeJpeg(bitmap, profile.Quality), bitmap.PixelWidth, bitmap.PixelHeight);
        }
        catch (Exception e) when (e is FileFormatException or NotSupportedException or ArgumentException) { return null; }
    }

    public static BitmapSource Resize(BitmapSource image, int longEdge)
    {
        var scale = Math.Min(1d, (double)longEdge / Math.Max(image.PixelWidth, image.PixelHeight));
        return scale < 1 ? new TransformedBitmap(image, new ScaleTransform(scale, scale)) : image;
    }

    public static byte[] EncodeJpeg(BitmapSource bitmap, int quality)
    {
        // A new frame strips camera orientation metadata; pixel orientation is already correct.
        bitmap = new FormatConvertedBitmap(bitmap, PixelFormats.Bgr24, null, 0);
        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    public static BitmapSource FlattenOnWhite(BitmapSource bitmap)
    {
        bitmap = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var width = bitmap.PixelWidth; var height = bitmap.PixelHeight;
        var rgba = new byte[checked(width * height * 4)];
        bitmap.CopyPixels(rgba, width * 4, 0);
        var rgb = new byte[checked(width * height * 3)];
        for (int source = 0, target = 0; source < rgba.Length; source += 4, target += 3)
        {
            var alpha = rgba[source + 3];
            rgb[target] = (byte)((rgba[source + 2] * alpha + 255 * (255 - alpha) + 127) / 255);
            rgb[target + 1] = (byte)((rgba[source + 1] * alpha + 255 * (255 - alpha) + 127) / 255);
            rgb[target + 2] = (byte)((rgba[source] * alpha + 255 * (255 - alpha) + 127) / 255);
        }
        return BitmapSource.Create(width, height, 96, 96, PixelFormats.Rgb24, null, rgb, width * 3);
    }
}
