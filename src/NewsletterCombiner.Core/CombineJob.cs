namespace NewsletterCombiner.Core;

public interface IDocumentConverter
{
    Task ConvertAsync(string source, string destination, CancellationToken cancellationToken, string? originalPath = null);
}

public record JobProgress(int Completed, int Total, string Message);

public sealed class CombineJob(IDocumentConverter converter, PdfSizeOptimizer? optimizer = null)
{
    public async Task<IReadOnlyList<string>> RunAsync(IReadOnlyList<OutputPlan> plans, string outputFolder,
        IProgress<JobProgress>? progress = null, CancellationToken token = default)
    {
        if (plans.Count == 0) throw new ArgumentException("Add at least one newsletter.");
        Directory.CreateDirectory(outputFolder);
        var destinations = plans.Select(p => Path.Combine(outputFolder, p.FileName)).ToArray();
        foreach (var destination in destinations)
            if (File.Exists(destination)) throw new IOException($"{Path.GetFileName(destination)} already exists. Choose another output folder or move the existing PDF.");
        var sources = plans.SelectMany(p => p.Sources).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var total = sources.Length + plans.Count;
        var completed = 0;
        var work = Path.Combine(outputFolder, ".newsletter-" + Guid.NewGuid().ToString("N"));
        var published = new List<string>();
        Directory.CreateDirectory(work);
        try
        {
            var converted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in sources)
            {
                token.ThrowIfCancellationRequested();
                progress?.Report(new(completed, total, $"Preparing {Path.GetFileName(source)}"));
                // Snapshot originals, including PDFs, so a file edited mid-run cannot differ across year outputs.
                var sourceFolder = Path.Combine(work, $"source-{completed}");
                var convertedFolder = Path.Combine(work, $"pdf-{completed}");
                Directory.CreateDirectory(sourceFolder);
                Directory.CreateDirectory(convertedFolder);
                var snapshot = Path.Combine(sourceFolder, Path.GetFileName(source));
                File.Copy(source, snapshot);
                var pdf = Path.Combine(convertedFolder, Path.GetFileNameWithoutExtension(source) + ".pdf");
                try
                {
                    if (Path.GetExtension(source).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) pdf = snapshot;
                    // Preserve the source location for resolving relative links in the read-only snapshot.
                    else await converter.ConvertAsync(snapshot, pdf, token, originalPath: source);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    throw new InvalidDataException($"{Path.GetFileName(source)}: {e.Message}", e);
                }
                converted.Add(source, pdf);
                completed++;
            }
            for (var i = 0; i < plans.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var plan = plans[i];
                progress?.Report(new(completed, total, $"Creating {plan.FileName}"));
                var output = Path.Combine(work, plan.FileName);
                PdfCombiner.Merge(plan.Sources.Select(s => converted[s]), output, Path.GetFileNameWithoutExtension(plan.FileName), token);
                optimizer?.Optimize(output, token, message => progress?.Report(new(completed, total, $"{message} · {plan.FileName}")));
                completed++;
            }
            token.ThrowIfCancellationRequested();
            // Commit only after every output is ready; never overwrite an existing file.
            foreach (var plan in plans)
            {
                var destination = Path.Combine(outputFolder, plan.FileName);
                File.Move(Path.Combine(work, plan.FileName), destination, overwrite: false);
                published.Add(destination);
            }
            progress?.Report(new(total, total, $"Saved {published.Count} PDF{(published.Count == 1 ? "" : "s")}"));
            return published;
        }
        catch
        {
            foreach (var destination in published)
            {
                try { File.Delete(destination); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            throw;
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
