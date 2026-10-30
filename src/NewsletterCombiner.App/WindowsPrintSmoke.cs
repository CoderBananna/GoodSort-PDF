using System.IO;
using System.Diagnostics;
using System.Printing;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NewsletterCombiner.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace NewsletterCombiner.App;

// Uses only Microsoft Print to PDF with explicit temporary output paths; never prints to paper.
internal static class WindowsPrintSmoke
{
    internal static async Task RunAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        if (Directory.EnumerateFileSystemEntries(folder).Any()) throw new IOException("Use an empty check folder.");
        var checks = new List<string>();
        void Check(bool pass, string message)
        {
            if (!pass) throw new InvalidOperationException(message);
            checks.Add(message); File.AppendAllText(Path.Combine(folder, "progress.txt"), message + Environment.NewLine);
        }
        var printer = await PdfPrinter.OnWorker(() =>
        {
            using var server = new LocalPrintServer();
            using var queue = server.GetPrintQueue("Microsoft Print to PDF");
            var ticket = queue.DefaultPrintTicket.Clone();
            ticket.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4);
            ticket.OutputColor = OutputColor.Color;
            ticket.CopyCount = 1;
            return PrinterSelection.FromQueue(queue, ticket);
        });
        var first = Path.Combine(folder, "Year 3 portrait.pdf");
        var second = Path.Combine(folder, "Year 4 landscape.pdf");
        var notice = Path.Combine(folder, "Notice.pdf");
        Create(first, 400, 600); Create(second, 600, 400); Create(notice, 400, 600);
        var window = new MainWindow(false) { ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000 };
        try
        {
            window.Show();
            Check(window.PrintAfterSavingCheckBox.IsChecked != true, "Printing is opt-in on every app launch");
            window.NewslettersPanel.AddPaths([first, second]); window.NoticesPanel.AddPaths([notice]);
            window.PrintAfterSavingCheckBox.IsChecked = true;
            var selections = 0; var printCalls = 0;
            window.SelectPrinter = () => { selections++; return printer; };
            foreach (var mode in new[] { ConversionMode.Newsletters, ConversionMode.Bulk, ConversionMode.Combine })
            {
                ((RadioButton)(mode == ConversionMode.Newsletters ? window.NewsletterMode : mode == ConversionMode.Bulk ? window.BulkMode : window.CombineMode)).IsChecked = true;
                var destination = Path.Combine(folder, mode.ToString());
                window.OutputFolder.Text = destination;
                var expected = window.BuildPlans(DateTime.Now).Select(p => p.FileName).ToArray();
                var captured = new List<string>();
                window.PrintSavedOutputs = async (paths, selected, progress, token) =>
                {
                    printCalls++; captured.AddRange(paths);
                    Check(paths.All(File.Exists), $"{mode}: all PDFs are saved before printing begins");
                    Check(!window.PrintAfterSavingCheckBox.IsEnabled && !window.GoButton.IsEnabled, $"{mode}: controls are locked during printing");
                    return await PdfPrinter.PrintAsync(paths, selected, progress, token, path => path + ".printed.pdf");
                };
                var before = selections;
                await window.RunBatchAsync(false);
                Check(selections == before + 1 && captured.Select(Path.GetFileName).SequenceEqual(expected), $"{mode}: selects once and prints each output in plan order");
                Check(window.StatusText.Text.Contains("sent to printer"), $"{mode}: reports spooled completion");
                foreach (var path in captured)
                {
                    await WaitForPdf(path + ".printed.pdf");
                    using var saved = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                    using var printed = PdfReader.Open(path + ".printed.pdf", PdfDocumentOpenMode.Import);
                    Check(saved.PageCount == printed.PageCount, $"{mode}: printed output retains all saved pages ({Path.GetFileName(path)})");
                }
                Capture(window, Path.Combine(folder, mode + ".png"));
            }
            await window.OpenEditorAsync(first, false);
            window.PageEditorControl.Duplicate(0);
            window.AddArrangedCheckBox.IsChecked = true;
            window.OutputFolder.Text = Path.Combine(folder, "Arrange");
            window.PrintSavedOutputs = async (paths, selected, progress, token) =>
            {
                printCalls++;
                Check(paths.Count == 1 && File.Exists(paths[0]), "Arrange: prints the saved arrangement");
                return await PdfPrinter.PrintAsync(paths, selected, progress, token, path => path + ".printed.pdf");
            };
            var arranged = await window.SaveArrangementAsync(false);
            Check(arranged != null && window.NewslettersPanel.Files.Any(f => f.Path == arranged), "Arrange: printing preserves replacement in the other modes");
            await WaitForPdf(arranged + ".printed.pdf");
            using (var printed = PdfReader.Open(arranged + ".printed.pdf", PdfDocumentOpenMode.Import))
                Check(printed.PageCount == 2, "Arrange: duplicated page appears in printed output");
            Check(selections == 4 && printCalls == 4, "Every mode uses one printer selection and one ordered print batch");
            window.Width = 1000; window.Height = 800; window.UpdateLayout();
            var content = (FrameworkElement)window.Content;
            var controls = new FrameworkElement[] { window.AddArrangedCheckBox, window.PrintAfterSavingCheckBox, window.GoButton };
            Check(controls.All(c => c.TransformToAncestor(content).TransformBounds(new Rect(c.RenderSize)).Right <= content.ActualWidth + 1), "Both checkboxes and GO fit at minimum window size");
            Capture(window, Path.Combine(folder, "Arrange-minimum.png"));
            window.BulkMode.IsChecked = true;
            window.NoticesPanel.Files.Clear(); window.NewslettersPanel.Files.Clear(); window.NewslettersPanel.AddPaths([first, second]);
            window.OutputFolder.Text = Path.Combine(folder, "pdf-only");
            window.PrintSavedOutputs = async (paths, selected, progress, token) =>
            {
                foreach (var path in paths) { using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import); Check(pdf.PageCount == 1, "PDF-only bulk output has no appended pages"); }
                return await PdfPrinter.PrintAsync(paths, selected, progress, token, path => path + ".printed.pdf");
            };
            await window.RunBatchAsync(false);
            Check(window.StatusText.Text.Contains("sent to printer"), "PDF-only bulk saves, compresses and spools without notices");
            var cancelledFolder = Path.Combine(folder, "dialog-cancelled");
            window.OutputFolder.Text = cancelledFolder;
            window.SelectPrinter = () => null;
            await window.RunBatchAsync(false);
            Check(!Directory.Exists(cancelledFolder), "Cancelling printer selection starts no conversion or print job");
            window.PrintAfterSavingCheckBox.IsChecked = false;
            window.SelectPrinter = () => throw new Exception("Unexpected printer dialog");
            window.PrintSavedOutputs = (_, _, _, _) => throw new Exception("Unexpected print job");
            window.OutputFolder.Text = Path.Combine(folder, "save-only");
            await window.RunBatchAsync(false);
            Check(Directory.GetFiles(window.OutputFolder.Text, "*.pdf").Length == 2, "Unchecked printing saves normally without contacting a printer");
            window.PrintAfterSavingCheckBox.IsChecked = true; window.SelectPrinter = () => printer;
            window.PrintSavedOutputs = (paths, _, _, _) => Task.FromResult(new PrintResult(1, paths.Count, false, "Printer disconnected"));
            window.OutputFolder.Text = Path.Combine(folder, "print-failure");
            await window.RunBatchAsync(false);
            Check(Directory.GetFiles(window.OutputFolder.Text, "*.pdf").Length == 2 && window.StatusText.Text.Contains("print failed"), "Print failure keeps every saved output and reports the partial batch");
            window.PrintSavedOutputs = (paths, _, _, _) => Task.FromResult(new PrintResult(1, paths.Count, true));
            window.OutputFolder.Text = Path.Combine(folder, "print-cancelled");
            await window.RunBatchAsync(false);
            Check(Directory.GetFiles(window.OutputFolder.Text, "*.pdf").Length == 2 && window.StatusText.Text.Contains("printing stopped"), "Cancelling printing preserves saved PDFs and submitted count");
            var broken = Path.Combine(folder, "Broken.pdf"); File.WriteAllText(broken, "not a PDF");
            window.NewslettersPanel.Files.Clear(); window.NewslettersPanel.AddPaths([broken]);
            window.OutputFolder.Text = Path.Combine(folder, "bad-input");
            var unexpectedPrint = false;
            window.PrintSavedOutputs = (paths, _, _, _) => { unexpectedPrint = true; return Task.FromResult(new PrintResult(0, paths.Count, false)); };
            try { await window.RunBatchAsync(false); } catch (Exception) { }
            Check(!unexpectedPrint && !Directory.EnumerateFiles(window.OutputFolder.Text, "*.pdf").Any(), "Conversion failure submits nothing to the printer");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            var result = await PdfPrinter.PrintAsync([first], printer, null, cancelled.Token, _ => Path.Combine(folder, "must-not-print.pdf"));
            Check(result.Cancelled && result.Submitted == 0 && !File.Exists(Path.Combine(folder, "must-not-print.pdf")), "Native print cancellation creates no output job");
            await using var preview = await PdfPreviewSession.OpenAsync(Path.Combine(folder, "pdf-only", "Year 4 landscape.pdf.printed.pdf"), CancellationToken.None);
            var thumb = await preview.ThumbnailAsync(0, CancellationToken.None);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(thumb));
            using (var output = File.Create(Path.Combine(folder, "printed-landscape-on-a4.png"))) encoder.Save(output);
            var many = Path.Combine(folder, "large-folder"); Directory.CreateDirectory(many);
            await Task.Run(() =>
            {
                var fixture = File.ReadAllBytes(first);
                for (var i = 0; i < 10_000; i++) File.WriteAllBytes(Path.Combine(many, $"Document-{i:00000}.pdf"), fixture);
            });
            window.NewslettersPanel.Files.Clear();
            var changeEvents = 0;
            window.NewslettersPanel.Changed += (_, _) => changeEvents++;
            var timer = Stopwatch.StartNew();
            window.NewslettersPanel.AddPaths([many]); timer.Stop();
            Check(window.NewslettersPanel.Files.Count == 10_000 && changeEvents == 1, "A 10,000-PDF folder imports in one list update");
            window.NewslettersPanel.AddPaths([many]);
            Check(window.NewslettersPanel.Files.Count == 10_000 && changeEvents == 1, "Re-dropping a large folder adds no duplicates or redundant refresh");
            File.WriteAllText(Path.Combine(folder, "results.json"), JsonSerializer.Serialize(new { Version = AppInfo.Version, Passed = checks.Count, Checks = checks, Import10000Milliseconds = timer.Elapsed.TotalMilliseconds, Printer = "Microsoft Print to PDF (temporary output files only)" }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { await window.PageEditorControl.DisposeAsync(); window.Close(); }
    }

    static async Task WaitForPdf(string path)
    {
        for (var i = 0; i < 100; i++)
        {
            try { using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import); if (pdf.PageCount > 0) return; } catch (Exception) { }
            await Task.Delay(200);
        }
        throw new IOException("The virtual printer did not finish: " + path);
    }
    static void Create(string path, double width, double height)
    {
        using var pdf = new PdfDocument(); var page = pdf.AddPage();
        page.Width = XUnit.FromPoint(width); page.Height = XUnit.FromPoint(height);
        using var g = XGraphics.FromPdfPage(page);
        g.DrawRectangle(new XPen(XColors.Red, 6), 6, 6, width - 12, height - 12);
        g.DrawRectangle(XBrushes.CornflowerBlue, 30, 30, width - 60, height / 3);
        g.DrawRectangle(XBrushes.Gold, width / 2, height / 2, width / 3, height / 3);
        pdf.Save(path);
    }
    static void Capture(Window window, string path)
    {
        var content = (FrameworkElement)window.Content; content.UpdateLayout();
        var width = (int)Math.Ceiling(content.ActualWidth + 60);
        var height = (int)Math.Ceiling(content.ActualHeight + 48);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle((Brush)window.FindResource("Paper"), null, new Rect(0, 0, width, height));
            context.DrawRectangle(new VisualBrush(content), null, new Rect(30, 24, content.ActualWidth, content.ActualHeight));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path); encoder.Save(output);
    }
}
