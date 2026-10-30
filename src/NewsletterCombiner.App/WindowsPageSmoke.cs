using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
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

internal static class WindowsPageSmoke
{
    internal static async Task RunAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        if (Directory.EnumerateFileSystemEntries(folder).Any()) throw new IOException("Use an empty check folder.");
        var checks = new List<string>();
        void Check(bool pass, string name) { if (!pass) throw new InvalidOperationException(name); checks.Add(name); File.AppendAllText(Path.Combine(folder, "progress.txt"), name + Environment.NewLine); }
        var source = Path.Combine(folder, "Year 3 Scouts information pack.pdf");
        CreatePdf(source, 8);
        var original = SHA256.HashData(File.ReadAllBytes(source));
        var window = new MainWindow(false) { ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000 };
        var editor = window.PageEditorControl;
        var metrics = new Dictionary<string, double>();
        try
        {
            window.Show();
            window.OutputFolder.Text = Path.Combine(folder, "output");
            var before = Path.Combine(folder, "Before.pdf"); CreatePdf(before, 1);
            var after = Path.Combine(folder, "After.pdf"); CreatePdf(after, 1);
            window.NewslettersPanel.AddPaths([before, source, after]);
            var origin = window.NewslettersPanel.Files[1]; origin.Year = "Prep"; origin.ConfigureGrouping(true, ["Joeys", "Cubs", "Scouts"]);
            await window.OpenEditorAsync(source, false);
            await Task.Delay(150); window.UpdateLayout(); await editor.WaitForPreviewsAsync();
            Check(window.Mode == ConversionMode.Arrange && editor.Pages.Count == 8 && window.FileColumns.Visibility == Visibility.Collapsed, "Arrange mode loads all page records and replaces the file columns");
            Check(window.AddArrangedCheckBox.Visibility == Visibility.Visible && window.AddArrangedCheckBox.IsChecked != true, "Optional add-to-other-modes checkbox appears beside GO");
            Check(editor.Pages.Take(4).All(p => p.Preview != null && p.Preview.PixelWidth <= 220 && p.Preview.PixelHeight <= 300), "Visible pages receive bounded native PDF thumbnails");
            var count = editor.Session!.RenderCount;
            var first = await editor.Session.ThumbnailAsync(0, CancellationToken.None);
            var again = await editor.Session.ThumbnailAsync(0, CancellationToken.None);
            Check(ReferenceEquals(first, again) && editor.Session.RenderCount == count, "Repeated preview requests reuse the cached bitmap");
            editor.MovePage(0, 3);
            editor.Duplicate(1);
            editor.ToggleRemoved(0);
            Check(editor.Pages[0].IsRemoved && editor.OutputOrder().SequenceEqual(new[] { 2, 2, 3, 0, 4, 5, 6, 7 }), "Reorder, duplicate and soft removal produce the expected page selection");
            editor.ToggleRemoved(0);
            Check(!editor.Pages[0].IsRemoved && editor.OutputOrder().Length == 9, "Undelete restores the original page in its current position");
            editor.ToggleRemoved(0);
            await Task.Delay(150); window.UpdateLayout(); await editor.WaitForPreviewsAsync();
            Capture(window, Path.Combine(folder, "arrange-pages.png"));
            // Exercise the actual right-click Remove/Undelete menu on a realized card.
            var card = Descendants(editor).OfType<FrameworkElement>().First(e => e.Focusable && e.DataContext == editor.Pages[0] && e.ContextMenu != null);
            card.ContextMenu!.PlacementTarget = card;
            var restore = (MenuItem)card.ContextMenu.Items[1];
            restore.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(!editor.Pages[0].IsRemoved, "Right-click menu undeletes the targeted page");
            restore.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(editor.Pages[0].IsRemoved, "Right-click menu marks the targeted page as removed again");
            window.ArrangedNameBox.Text = "Arranged pack";
            var listCount = window.NewslettersPanel.Files.Count;
            var saved = await window.SaveArrangementAsync(false);
            Check(saved != null && File.Exists(saved) && window.NewslettersPanel.Files.Count == listCount && origin.Path == source, "Unchecked handoff saves a new PDF without adding it to the lists");
            using (var pdf = PdfReader.Open(saved!, PdfDocumentOpenMode.Import))
            {
                var order = Enumerable.Range(0, pdf.PageCount).Select(i => (int)pdf.Pages[i].Width.Point).ToArray();
                Check(order.SequenceEqual(new[] { 602, 602, 603, 600, 604, 605, 606, 607 }), "Saved PDF contains kept pages in their chosen order including duplicates");
                Check(pdf.Pages[3].Elements.GetArray("/Annots")!.Elements.GetDictionary(0)!.Elements.GetDictionary("/A")!.Elements.GetString("/URI") == "https://example.org/pack", "Arranged PDF retains a web hyperlink");
            }
            Check(SHA256.HashData(File.ReadAllBytes(source)).SequenceEqual(original), "Editor and save leave original PDF bytes unchanged");
            window.ArrangedNameBox.Text = "Arranged pack for lists";
            window.AddArrangedCheckBox.IsChecked = true;
            var shared = await window.SaveArrangementAsync(false);
            Check(window.NewslettersPanel.Files.Count == listCount && window.NewslettersPanel.Files[1] == origin && origin.Path == shared && origin.Year == "Prep" && origin.CustomGroup == "Scouts", "Checked handoff replaces its source entry in place and retains year/custom assignments");
            Check(window.NewslettersPanel.Files.Select(f => f.Path).SequenceEqual(new[] { before, shared!, after }), "Replacement retains neighbouring files and list order without a duplicate");
            var collisionRejected = false;
            try { await window.SaveArrangementAsync(false); } catch (IOException) { collisionRejected = true; }
            Check(collisionRejected && origin.Path == shared && window.NewslettersPanel.Files.Count == listCount, "A failed save leaves the current list entry unchanged");
            window.ArrangedNameBox.Text = "Arranged pack revision two";
            shared = await window.SaveArrangementAsync(false);
            Check(window.NewslettersPanel.Files.Count == listCount && window.NewslettersPanel.Files[1] == origin && origin.Path == shared && SHA256.HashData(File.ReadAllBytes(source)).SequenceEqual(original), "Repeated checked saves update the same entry and leave the original disk file unchanged");
            window.CustomGroupsChoice.IsChecked = true;
            Check(origin.CustomGroup == "Scouts", "A group inferred from the original filename survives renaming and reconfiguring grouping");
            foreach (var choice in new[] { window.NewsletterMode, window.BulkMode, window.CombineMode })
            {
                choice.IsChecked = true;
                Check(window.NewslettersPanel.Files.Any(f => f.Path == shared) && window.AddArrangedCheckBox.Visibility == Visibility.Collapsed, "Saved PDF is available in " + choice.Content);
            }
            window.BulkMode.IsChecked = true;
            await Task.Delay(350); window.UpdateLayout();
            var fileCard = Descendants(window.NewslettersPanel).OfType<Border>().First(e => e.DataContext == origin && e.ContextMenu != null);
            fileCard.ContextMenu!.PlacementTarget = fileCard;
            ((MenuItem)fileCard.ContextMenu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            for (var i = 0; i < 200 && (editor.SourcePath != shared || !window.GoButton.IsEnabled); i++) await Task.Delay(25);
            Check(window.Mode == ConversionMode.Arrange && editor.SourcePath == shared, "Right-clicking a PDF in the left list opens that file in Arrange pages");
            for (var i = 0; i < editor.Pages.Count; i++) editor.ToggleRemoved(i);
            Check(!window.GoButton.IsEnabled, "Removing every page disables GO");
            editor.ToggleRemoved(0);
            Check(window.GoButton.IsEnabled, "Undeleting a page re-enables GO");
            editor.MarkSaved();
            var oldFolder = editor.Session!.WorkFolder;
            var bad = Path.Combine(folder, "bad.pdf"); File.WriteAllText(bad, "not a PDF");
            try { await window.OpenEditorAsync(bad, false); throw new InvalidOperationException("Invalid PDF accepted"); }
            catch (Exception) when (editor.SourcePath == shared) { }
            Check(editor.SourcePath == shared && editor.Pages.Count > 0, "Failed replacement keeps the current page arrangement");
            var png = Path.Combine(folder, "notice.png"); CreateImage(png);
            await window.OpenEditorAsync(png, false);
            Check(editor.Pages.Count == 1 && File.Exists(editor.PdfPath), "A dropped image is converted to PDF before editing");
            Check(!Directory.Exists(oldFolder), "Replacing the document cleans up the previous editor snapshot");
            window.ArrangedNameBox.Text = "Standalone notice";
            var standalone = await window.SaveArrangementAsync(false);
            Check(window.NewslettersPanel.Files.Count == listCount + 1 && window.NewslettersPanel.Files[^1].Path == standalone, "A directly opened document with no source entry is added to the shared list");
            window.ArrangedNameBox.Text = "Standalone notice revised";
            standalone = await window.SaveArrangementAsync(false);
            Check(window.NewslettersPanel.Files.Count == listCount + 1 && window.NewslettersPanel.Files[^1].Path == standalone, "Further saves of a directly opened document replace its newly added entry");
            var appended = Path.Combine(folder, "Appended notice.pdf"); CreatePdf(appended, 2);
            window.NoticesPanel.AddPaths([appended]);
            var appendedEntry = window.NoticesPanel.Files[0];
            window.CombineMode.IsChecked = true;
            await window.OpenEditorAsync(appended, false);
            editor.ToggleRemoved(0);
            window.ArrangedNameBox.Text = "Edited appended notice";
            var appendedOutput = await window.SaveArrangementAsync(false);
            Check(window.NoticesPanel.Files.Count == 1 && window.NoticesPanel.Files[0] == appendedEntry && appendedEntry.Path == appendedOutput && window.NewslettersPanel.Files.Count == listCount + 1, "Editing an appended item from Combine all replaces that item in its original notices column");
            window.CombineMode.IsChecked = true;
            Check(window.NewslettersPanel.Files[^1] == appendedEntry && appendedEntry.IsAppended && appendedEntry.Path == appendedOutput, "Edited appended PDF retains its colour and appended role when returning to Combine all");
            var rtf = Path.Combine(folder, "Meeting notes.rtf");
            File.WriteAllText(rtf, @"{\rtf1\ansi GoodSort PDF conversion check.\page Second page for arranging.}");
            await window.OpenEditorAsync(rtf, false);
            Check(editor.Pages.Count == 2 && File.Exists(editor.PdfPath), "A Word-compatible document is exported through desktop Word before editing");
            var large = Path.Combine(folder, "Large pack.pdf"); CreatePdf(large, 400);
            var stopwatch = Stopwatch.StartNew();
            await window.OpenEditorAsync(large, false);
            metrics["open_400_pages_ms"] = stopwatch.Elapsed.TotalMilliseconds;
            await Task.Delay(150); window.UpdateLayout(); await editor.WaitForPreviewsAsync();
            metrics["first_visible_thumbnails_400_pages_ms"] = stopwatch.Elapsed.TotalMilliseconds;
            metrics["initial_rendered_pages"] = editor.Session!.RenderCount;
            Check(editor.Pages.Count == 400 && editor.Pages.Count(p => p.Preview != null) < 32 && editor.Session.RenderCount < 40, "Large PDFs virtualize cards and render only visible/nearby pages");
            var scroll = Descendants(editor.PageRows).OfType<ScrollViewer>().First();
            scroll.ScrollToEnd(); window.UpdateLayout(); await Task.Delay(150); await editor.WaitForPreviewsAsync();
            Check(editor.Pages[^1].Preview != null && editor.Pages[0].Preview == null, "Scrolling recycles cards and loads the final page without retaining offscreen previews");
            scroll.ScrollToHome(); window.UpdateLayout(); await Task.Delay(150); await editor.WaitForPreviewsAsync();
            Check(editor.Pages[0].Preview != null && editor.Pages[^1].Preview == null, "Scrolling back restores previews on recycled cards");
            for (var i = 0; i < 70; i++) await editor.Session.ThumbnailAsync(i, CancellationToken.None);
            Check(editor.Session.CachedCount <= PdfPreviewSession.CacheLimit, "Thumbnail cache remains bounded after many page requests");
            metrics["thumbnail_cache_entries"] = editor.Session.CachedCount;
            window.Width = 1000; window.Height = 800; window.UpdateLayout();
            await Task.Delay(150); await editor.WaitForPreviewsAsync();
            var content = (FrameworkElement)window.Content;
            Check(window.GoButton.TransformToAncestor(content).TransformBounds(new Rect(window.GoButton.RenderSize)).Bottom <= content.ActualHeight + 1, "Arrange mode keeps GO visible at minimum window size");
            Capture(window, Path.Combine(folder, "arrange-minimum.png"));
            var finalWork = editor.Session.WorkFolder;
            await editor.DisposeAsync();
            Check(!Directory.Exists(finalWork), "Closing the editor releases its preview stream and temporary snapshot");
            File.WriteAllText(Path.Combine(folder, "results.json"), JsonSerializer.Serialize(new { Version = AppInfo.Version, Passed = checks.Count, Checks = checks, Metrics = metrics }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { editor.MarkSaved(); await editor.DisposeAsync(); window.Close(); }
    }
    static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
    static void CreatePdf(string path, int count)
    {
        using var pdf = new PdfDocument();
        for (var i = 0; i < count; i++)
        {
            var page = pdf.AddPage(); page.Width = XUnit.FromPoint(600 + i); page.Height = XUnit.FromPoint(800);
            using var graphics = XGraphics.FromPdfPage(page);
            var colour = XColor.FromArgb(255, 25 + i * 47 % 180, 65 + i * 37 % 170, 100 + i * 29 % 150);
            graphics.DrawRectangle(new XSolidBrush(colour), 35, 35, 530, 115);
            for (var row = 0; row < 12; row++) graphics.DrawRectangle(XBrushes.LightGray, 35, 180 + row * 28, row % 3 == 0 ? 450 : 510, 9);
            graphics.DrawRectangle(new XSolidBrush(colour), 35, 570, 235, 155);
            graphics.DrawRectangle(XBrushes.Gainsboro, 295, 570, 270, 155);
        }
        pdf.Pages[0].AddWebLink(new PdfRectangle(new XPoint(35, 35), new XPoint(565, 150)), "https://example.org/pack");
        pdf.Save(path);
    }
    static void CreateImage(string path)
    {
        var pixels = new byte[100 * 200 * 3]; Array.Fill(pixels, (byte)180);
        var bitmap = BitmapSource.Create(100, 200, 96, 96, PixelFormats.Rgb24, null, pixels, 300);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
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
