namespace NewsletterCombiner.Core;

public sealed class PageArrangementJob(PdfSizeOptimizer? optimizer = null)
{
    public string Save(string sourcePdf, IReadOnlyList<int> order, string outputFolder, string fileName, CancellationToken token = default, Action<string>? status = null)
    {
        if (order.Count == 0) throw new ArgumentException("Keep at least one page before saving.");
        if (Path.GetFileName(fileName) != fileName || !fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a PDF filename without a folder path.");
        Directory.CreateDirectory(outputFolder);
        var destination = Path.GetFullPath(Path.Combine(outputFolder, fileName));
        if (File.Exists(destination)) throw new IOException($"{fileName} already exists. Choose another PDF name or folder.");
        var work = Path.Combine(outputFolder, ".goodsort-pages-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var staged = Path.Combine(work, "arranged.pdf");
            status?.Invoke("Saving arranged pages…");
            PdfCombiner.Arrange(sourcePdf, order, staged, Path.GetFileNameWithoutExtension(fileName), token);
            optimizer?.Optimize(staged, token, status);
            token.ThrowIfCancellationRequested();
            File.Move(staged, destination, overwrite: false);
            return destination;
        }
        finally
        {
            try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
