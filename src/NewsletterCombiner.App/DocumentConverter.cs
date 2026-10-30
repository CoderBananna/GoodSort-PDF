using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NewsletterCombiner.Core;

namespace NewsletterCombiner.App;

public sealed class DocumentConverter : IDocumentConverter
{
    public async Task ConvertAsync(string source, string destination, CancellationToken cancellationToken, string? originalPath = null)
    {
        var extension = Path.GetExtension(source).ToLowerInvariant();
        if (extension is ".jpg" or ".jpeg" or ".png" or ".bmp")
        {
            ConvertImage(source, destination);
            return;
        }
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("--office-export");
        info.ArgumentList.Add(source);
        info.ArgumentList.Add(destination);
        info.ArgumentList.Add(originalPath ?? source);
        using var process = Process.Start(info) ?? throw new IOException("Could not start document conversion.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException)
        {
            // Only stop our own worker, never kill the user's Word or PowerPoint process.
            if (!process.HasExited) process.Kill(entireProcessTree: false);
            await process.WaitForExitAsync(CancellationToken.None);
            cancellationToken.ThrowIfCancellationRequested();
            throw new IOException("Office took too long. Close any Word or PowerPoint prompts, check this file opens normally, and try again.");
        }
        if (process.ExitCode != 0 || !File.Exists(destination))
        {
            var error = destination + ".error.txt";
            throw new IOException(File.Exists(error) ? File.ReadAllText(error) : "Office could not export this file. Check that it opens in desktop Word or PowerPoint.");
        }
    }

    static void ConvertImage(string source, string destination)
    {
        using var input = File.OpenRead(source);
        var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        BitmapSource bitmap = decoder.Frames[0];
        var frame = decoder.Frames[0];
        // Convert tagged images to sRGB before embedding; do not silently discard their colour profile.
        if (frame.ColorContexts is { Count: > 0 })
            bitmap = new ColorConvertedBitmap(bitmap, frame.ColorContexts[0], new ColorContext(PixelFormats.Bgra32), PixelFormats.Bgra32);
        ushort orientation = 1;
        if (frame.Metadata is BitmapMetadata metadata)
        {
            try { orientation = Convert.ToUInt16(metadata.GetQuery("/app1/ifd/{ushort=274}") ?? 1); }
            catch (NotSupportedException) { }
        }
        var transform = orientation switch
        {
            2 => new Matrix(-1, 0, 0, 1, 0, 0),
            3 => new Matrix(-1, 0, 0, -1, 0, 0),
            4 => new Matrix(1, 0, 0, -1, 0, 0),
            5 => new Matrix(0, 1, 1, 0, 0, 0),
            6 => new Matrix(0, 1, -1, 0, 0, 0),
            7 => new Matrix(0, -1, -1, 0, 0, 0),
            8 => new Matrix(0, -1, 1, 0, 0, 0),
            _ => Matrix.Identity
        };
        if (!transform.IsIdentity) bitmap = new TransformedBitmap(bitmap, new MatrixTransform(transform));
        // Keep full page coverage, but avoid embedding camera-sized images in an A4 document.
        bitmap = WpfRasterCodec.Resize(bitmap, 2480);
        bitmap = WpfRasterCodec.FlattenOnWhite(bitmap);
        var png = destination + ".png";
        var jpg = destination + ".jpg";
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = File.Create(png)) encoder.Save(output);
            File.WriteAllBytes(jpg, WpfRasterCodec.EncodeJpeg(bitmap, 90));
            // Simple artwork and screenshots often compress better losslessly; photographs usually favour JPEG.
            PdfCombiner.ImageToPdf(new FileInfo(png).Length <= new FileInfo(jpg).Length ? png : jpg, destination);
        }
        finally { if (File.Exists(png)) File.Delete(png); if (File.Exists(jpg)) File.Delete(jpg); }
    }
}
