using System.IO;
using System.Windows.Media.Imaging;
using NewsletterCombiner.Core;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace NewsletterCombiner.App;

// All native PDF access is serialized off the UI thread. Only small, visible-page previews are requested.
internal sealed class PdfPreviewSession : IAsyncDisposable
{
    readonly SemaphoreSlim renderGate = new(1, 1);
    readonly Dictionary<int, (BitmapSource Image, LinkedListNode<int> Node)> cache = [];
    readonly LinkedList<int> recent = [];
    PdfDocument? document;
    IRandomAccessStream? input;
    bool disposed;
    public const int CacheLimit = 48;
    public string WorkFolder { get; }
    public string SourcePath { get; }
    public string PdfPath { get; private set; } = "";
    public int PageCount { get; private set; }
    public int RenderCount { get; private set; }
    public int CachedCount => cache.Count;

    PdfPreviewSession(string path)
    {
        SourcePath = Path.GetFullPath(path);
        WorkFolder = Path.Combine(Path.GetTempPath(), "GoodSort-pages-" + Guid.NewGuid().ToString("N"));
    }

    public static async Task<PdfPreviewSession> OpenAsync(string source, CancellationToken token)
    {
        var session = new PdfPreviewSession(source);
        try
        {
            await Task.Run(async () =>
            {
                if (!NewsletterPlan.Extensions.Contains(Path.GetExtension(source))) throw new ArgumentException("Choose a PDF, Word document, PowerPoint presentation or supported image.");
                Directory.CreateDirectory(session.WorkFolder);
                var snapshot = Path.Combine(session.WorkFolder, "source" + Path.GetExtension(source));
                await using (var original = File.OpenRead(source))
                await using (var copy = File.Create(snapshot)) await original.CopyToAsync(copy, token);
                session.PdfPath = snapshot;
                if (!Path.GetExtension(source).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    session.PdfPath = Path.Combine(session.WorkFolder, "converted.pdf");
                    await new DocumentConverter().ConvertAsync(snapshot, session.PdfPath, token, originalPath: source);
                }
                token.ThrowIfCancellationRequested();
                session.input = File.OpenRead(session.PdfPath).AsRandomAccessStream();
                session.document = await PdfDocument.LoadFromStreamAsync(session.input).AsTask(token);
                session.PageCount = checked((int)session.document.PageCount);
                using var check = PdfSharp.Pdf.IO.PdfReader.Open(session.PdfPath, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
                if (session.PageCount == 0 || check.PageCount != session.PageCount) throw new InvalidDataException("This PDF could not be prepared consistently. Try exporting it again.");
            }, token);
            return session;
        }
        catch { await session.DisposeAsync(); throw; }
    }

    public Task<BitmapSource> ThumbnailAsync(int index, CancellationToken token) => Task.Run(async () =>
    {
        await renderGate.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            token.ThrowIfCancellationRequested();
            if (cache.TryGetValue(index, out var found))
            {
                recent.Remove(found.Node); recent.AddLast(found.Node);
                return found.Image;
            }
            using var page = document!.GetPage((uint)index);
            using var stream = new InMemoryRandomAccessStream();
            var scale = Math.Min(220d / Math.Max(1, page.Size.Width), 300d / Math.Max(1, page.Size.Height));
            var options = new PdfPageRenderOptions
            {
                DestinationWidth = (uint)Math.Max(1, Math.Round(page.Size.Width * scale)),
                DestinationHeight = (uint)Math.Max(1, Math.Round(page.Size.Height * scale)),
                BackgroundColor = new Windows.UI.Color { A = 255, R = 255, G = 255, B = 255 }
            };
            // Let an in-flight native render finish before disposal. Cancel queued/offscreen requests instead.
            await page.RenderToStreamAsync(stream, options);
            token.ThrowIfCancellationRequested();
            stream.Seek(0);
            using var managed = stream.AsStreamForRead();
            var image = new BitmapImage();
            image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = managed; image.EndInit(); image.Freeze();
            RenderCount++;
            cache[index] = (image, recent.AddLast(index));
            while (cache.Count > CacheLimit)
            {
                var oldest = recent.First!; cache.Remove(oldest.Value); recent.RemoveFirst();
            }
            return (BitmapSource)image;
        }
        finally { renderGate.Release(); }
    }, token);

    public async ValueTask DisposeAsync()
    {
        await renderGate.WaitAsync();
        try
        {
            if (disposed) return;
            disposed = true;
            cache.Clear(); recent.Clear(); document = null;
            input?.Dispose(); input = null;
            try { Directory.Delete(WorkFolder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        finally { renderGate.Release(); }
    }
}
