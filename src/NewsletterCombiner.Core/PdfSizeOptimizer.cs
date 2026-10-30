using System.IO.Compression;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace NewsletterCombiner.Core;

public record RasterInput(byte[] Data, int Width, int Height, bool IsJpeg, bool IsGrayscale);
public record EncodedRaster(byte[] Jpeg, int Width, int Height);
public record CompressionProfile(int LongEdge, int Quality, int Dpi = 200);
public interface IRasterCodec
{
    EncodedRaster? Encode(RasterInput image, CompressionProfile profile);
}
public record CompressionResult(long OriginalBytes, long FinalBytes)
{
    public bool OverTarget => FinalBytes > PdfSizeOptimizer.TargetBytes;
}

/// <summary>Recompresses image objects, never renders whole pages or changes their links or layout.</summary>
public sealed class PdfSizeOptimizer(IRasterCodec? codec = null)
{
    public const long TargetBytes = 10_000_000;
    const long PreferredBytes = 9_500_000;
    static readonly CompressionProfile[] Profiles = [new(2480, 90, 200), new(2100, 85, 180), new(1800, 80, 160), new(1754, 76, 150)];

    public CompressionResult Optimize(string path, CancellationToken token = default, Action<string>? status = null)
    {
        var originalSize = new FileInfo(path).Length;
        var bestSize = originalSize;
        string? best = null;
        var scratch = path + ".compression-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(scratch);
        try
        {
            token.ThrowIfCancellationRequested();
            status?.Invoke("Compressing PDF");
            var lossless = Path.Combine(scratch, "lossless.pdf");
            using (var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Modify))
            {
                Configure(pdf);
                CompressStreams(pdf, token);
                pdf.Save(lossless);
            }
            if (new FileInfo(lossless).Length < bestSize) { best = lossless; bestSize = new FileInfo(lossless).Length; }
            var baseline = best ?? path;
            if (bestSize > TargetBytes && codec != null)
            {
                for (var pass = 0; pass < Profiles.Length; pass++)
                {
                    token.ThrowIfCancellationRequested();
                    status?.Invoke($"Reducing images · pass {pass + 1} of {Profiles.Length}");
                    // Every attempt starts from the untouched baseline, avoiding cumulative JPEG damage.
                    using var pdf = PdfReader.Open(baseline, PdfDocumentOpenMode.Modify);
                    Configure(pdf);
                    var changed = OptimizeImages(pdf, Profiles[pass], token);
                    if (changed == 0) continue;
                    var candidate = Path.Combine(scratch, $"images-{pass}.pdf");
                    pdf.Save(candidate);
                    var size = new FileInfo(candidate).Length;
                    if (size < bestSize) { best = candidate; bestSize = size; }
                    if (bestSize <= PreferredBytes) break;
                }
            }
            token.ThrowIfCancellationRequested();
            if (best != null) File.Move(best, path, overwrite: true);
            return new(originalSize, bestSize);
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }

    public static void Configure(PdfDocument pdf)
    {
        pdf.Options.CompressContentStreams = true;
        pdf.Options.FlateEncodeMode = PdfFlateEncodeMode.BestCompression;
        pdf.Options.UseFlateDecoderForJpegImages = PdfUseFlateDecoderForJpegImages.Never;
    }

    static PdfItem? Resolve(PdfItem? value) => value is PdfReference reference ? reference.Value : value;

    static void CompressStreams(PdfDocument pdf, CancellationToken token)
    {
        foreach (var item in pdf.Internals.GetAllObjects().OfType<PdfDictionary>())
        {
            token.ThrowIfCancellationRequested();
            if (item.Stream == null || item.Stream.Length < 256 || item.Elements.ContainsKey("/DecodeParms")) continue;
            var filter = Resolve(item.Elements["/Filter"]);
            if (filter != null && filter is not PdfName { Value: "/FlateDecode" }) continue;
            byte[] raw;
            try { raw = item.Stream.UnfilteredValue; }
            catch (Exception e) when (e is InvalidOperationException or NotSupportedException or InvalidDataException) { continue; }
            using var stream = new MemoryStream();
            using (var zip = new ZLibStream(stream, CompressionLevel.SmallestSize, leaveOpen: true)) zip.Write(raw);
            var compressed = stream.ToArray();
            if (compressed.Length >= item.Stream.Length) continue;
            item.Stream.Value = compressed;
            item.Elements.SetName("/Filter", "/FlateDecode");
            item.Elements.SetInteger("/Length", compressed.Length);
        }
    }

    int OptimizeImages(PdfDocument pdf, CompressionProfile profile, CancellationToken token)
    {
        var placements = PdfImagePlacement.Find(pdf, token);
        var images = pdf.Internals.GetAllObjects().OfType<PdfDictionary>()
            .Where(o => o.Elements.GetName("/Subtype") == "/Image").ToArray();
        var masks = images.SelectMany(o => new[] { Resolve(o.Elements["/SMask"]), Resolve(o.Elements["/Mask"]) })
            .OfType<PdfDictionary>().ToHashSet();
        var count = 0;
        foreach (var image in images)
        {
            token.ThrowIfCancellationRequested();
            // Leave transparency, special colour spaces and custom decoding untouched rather than change their appearance.
            if (masks.Contains(image) || image.Stream == null || image.Stream.Length < 65_536 ||
                image.Elements.ContainsKey("/SMask") || image.Elements.ContainsKey("/Mask") ||
                image.Elements.ContainsKey("/Decode") || image.Elements.GetBoolean("/ImageMask") ||
                image.Elements.GetInteger("/BitsPerComponent") != 8) continue;
            var colour = Resolve(image.Elements["/ColorSpace"]);
            var rgbProfile = colour is PdfArray profileArray && profileArray.Elements.Count == 2 &&
                Resolve(profileArray.Elements[0]) is PdfName { Value: "/ICCBased" } &&
                Resolve(profileArray.Elements[1]) is PdfDictionary profileData && profileData.Elements.GetInteger("/N") == 3;
            if (colour is not PdfName { Value: "/DeviceRGB" or "/DeviceGray" } && !rgbProfile) continue;
            var width = image.Elements.GetInteger("/Width");
            var height = image.Elements.GetInteger("/Height");
            if (width < 1 || height < 1 || (long)width * height > 100_000_000) continue;
            var filter = Resolve(image.Elements["/Filter"]);
            var jpeg = filter is PdfName { Value: "/DCTDecode" };
            if (!jpeg && filter != null && filter is not PdfName { Value: "/FlateDecode" }) continue;
            if (jpeg && image.Elements.ContainsKey("/DecodeParms")) continue;
            var gray = colour is PdfName { Value: "/DeviceGray" };
            byte[] data;
            try { data = jpeg ? image.Stream.Value : image.Stream.UnfilteredValue; }
            catch (Exception e) when (e is InvalidOperationException or NotSupportedException or InvalidDataException) { continue; }
            if (!jpeg && data.LongLength != (long)width * height * (gray ? 1 : 3)) continue;
            var imageProfile = profile;
            if (placements.TryGetValue(image, out var size))
            {
                var scale = Math.Max(size.Width * profile.Dpi / (72 * width), size.Height * profile.Dpi / (72 * height));
                imageProfile = profile with { LongEdge = Math.Max(1, Math.Min(profile.LongEdge, (int)Math.Ceiling(Math.Max(width, height) * Math.Min(1, scale)))) };
            }
            var encoded = codec!.Encode(new(data, width, height, jpeg, gray), imageProfile);
            if (encoded == null || encoded.Jpeg.Length >= image.Stream.Length) continue;
            image.Stream.Value = encoded.Jpeg;
            image.Elements.SetInteger("/Length", encoded.Jpeg.Length);
            image.Elements.SetName("/Filter", "/DCTDecode");
            image.Elements.Remove("/DecodeParms");
            // Keep RGB ICC profiles: the codec changes sample storage, not the colour interpretation.
            if (!rgbProfile) image.Elements.SetName("/ColorSpace", "/DeviceRGB");
            image.Elements.SetInteger("/Width", encoded.Width);
            image.Elements.SetInteger("/Height", encoded.Height);
            count++;
        }
        return count;
    }
}
