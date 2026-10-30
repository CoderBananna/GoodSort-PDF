namespace NewsletterCombiner.Core;

public interface IPdfPrintSpooler
{
    Task WaitForCapacityAsync(CancellationToken token);
    Task SubmitAsync(string pdfPath, CancellationToken token);
}

public record PrintProgress(int Submitted, int Total, string Message);
public record PrintResult(int Submitted, int Total, bool Cancelled, string? Error = null);

// Only receives published PDFs. A print failure must never roll back saved documents.
public static class PrintBatch
{
    public static async Task<PrintResult> RunAsync(IReadOnlyList<string> outputs, IPdfPrintSpooler spooler,
        IProgress<PrintProgress>? progress = null, CancellationToken token = default)
    {
        var submitted = 0;
        string? current = null;
        try
        {
            foreach (var output in outputs)
            {
                current = Path.GetFileName(output);
                token.ThrowIfCancellationRequested();
                progress?.Report(new(submitted, outputs.Count, $"Printing {submitted + 1} of {outputs.Count} · {current}"));
                await spooler.WaitForCapacityAsync(token);
                token.ThrowIfCancellationRequested();
                await spooler.SubmitAsync(output, token);
                submitted++;
                progress?.Report(new(submitted, outputs.Count, $"Sent {submitted} of {outputs.Count} PDFs to printer"));
            }
            return new(submitted, outputs.Count, false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return new(submitted, outputs.Count, true); }
        catch (Exception ex) { return new(submitted, outputs.Count, false, $"{current}: {ex.Message}"); }
    }
}
