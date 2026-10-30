using System.IO;
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

// Packaged Windows checks, invoked explicitly with --mode-smoke <empty temporary folder>.
// Uses generated fixtures and never loads or saves the user's preferences.
internal static class WindowsModeSmoke
{
    internal static async Task RunAsync(string folder)
    {
        // Match FilePanel's canonical paths even when the CI caller uses forward slashes.
        folder = Path.GetFullPath(folder);
        Directory.CreateDirectory(folder);
        if (Directory.EnumerateFileSystemEntries(folder).Any()) throw new IOException("Use an empty test folder.");
        var checks = new List<string>();
        void Check(bool pass, string description)
        {
            if (!pass) throw new InvalidOperationException(description);
            checks.Add(description);
        }
        var input = Path.Combine(folder, "inputs"); Directory.CreateDirectory(input);
        var portrait = Path.Combine(input, "Year 3 Class.png");
        var landscape = Path.Combine(input, "Year 4 Class.png");
        var appendix = Path.Combine(input, "Community notice.pdf");
        Image(portrait, 300, 600); Image(landscape, 600, 300);
        using (var pdf = new PdfDocument())
        {
            var page = pdf.AddPage(); page.Width = XUnit.FromPoint(321); page.Height = XUnit.FromPoint(500);
            using (var graphics = XGraphics.FromPdfPage(page)) graphics.DrawRectangle(XBrushes.CornflowerBlue, 15, 15, 100, 100);
            page.AddWebLink(new PdfRectangle(new XPoint(15, 15), new XPoint(115, 115)), "https://example.org/notice");
            pdf.Save(appendix);
        }
        var timestamp = new DateTime(2026, 10, 30, 14, 5, 0);
        var window = new MainWindow(persistPreferences: false) { ShowInTaskbar = false };
        try
        {
            window.OutputFolder.Text = @"C:\PDF output";
            window.NewslettersPanel.AddPaths([portrait, landscape]);
            window.NoticesPanel.AddPaths([appendix]);
            window.TermBox.SelectedItem = 4;
            window.FirstWeekBox.SelectedItem = 5;
            Check(window.Mode == ConversionMode.Newsletters && window.NamingControls.Visibility == Visibility.Visible, "Newsletter mode keeps term/week controls");
            Capture(window, Path.Combine(folder, "newsletters.png"));
            window.NewslettersPanel.Files[0].Year = "Prep";
            window.BulkMode.IsChecked = true;
            Check(window.Mode == ConversionMode.Bulk && window.NamingControls.Visibility == Visibility.Collapsed, "Bulk mode hides term/week controls");
            Check(window.NewslettersPanel.Files.All(f => f.YearVisibility == Visibility.Collapsed), "Bulk mode hides year fields for already loaded files");
            Check(window.GoButton.IsEnabled && window.SuffixBox.Visibility == Visibility.Visible, "Bulk mode enables conversion without year labels");
            window.SuffixBox.Text = "_ready_{date}_{n}";
            window.NewslettersPanel.FileList.SelectedIndex = 1;
            window.NewslettersPanel.UpButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(window.NewslettersPanel.Files[0].Path == landscape, "Move-up control changes displayed file order");
            var bulk = window.BuildPlans(timestamp);
            Check(bulk.Count == 2 && bulk[0].FileName == "Year 4 Class_ready_2026-10-30_001.pdf", "Bulk filename variables follow displayed order");
            Check(bulk.All(p => p.Sources.Count == 2 && p.Sources[^1] == appendix), "Bulk appends optional document to each PDF");
            Capture(window, Path.Combine(folder, "bulk.png"));
            var bulkOutputs = await Task.Run(() => new CombineJob(new DocumentConverter(), new PdfSizeOptimizer(new WpfRasterCodec())).RunAsync(bulk, Path.Combine(folder, "bulk-output")));
            Check(bulkOutputs.Count == 2, "Windows bulk conversion creates two PDFs");
            using (var pdf = PdfReader.Open(bulkOutputs[0], PdfDocumentOpenMode.Import))
            {
                Check(pdf.PageCount == 2 && pdf.Pages[0].Width.Point > pdf.Pages[0].Height.Point && Math.Abs(pdf.Pages[0].Width.Point - 841.8898) < 1, "Landscape image is fitted to A4 before appended page");
                Check((int)pdf.Pages[1].Width.Point == 321 && pdf.Pages[1].Elements.GetArray("/Annots")!.Elements.GetDictionary(0)!.Elements.GetDictionary("/A")!.Elements.GetString("/URI") == "https://example.org/notice", "Appended hyperlink survives native conversion and compression");
            }
            using (var pdf = PdfReader.Open(bulkOutputs[1], PdfDocumentOpenMode.Import))
                Check(pdf.Pages[0].Height.Point > pdf.Pages[0].Width.Point, "Portrait image keeps its orientation");
            window.CombineMode.IsChecked = true;
            window.CombinedNameBox.Text = "Combined pack {date}";
            Check(window.Mode == ConversionMode.Combine && window.CombinedNameBox.Visibility == Visibility.Visible && window.SuffixBox.Visibility == Visibility.Collapsed, "Combine mode offers one output filename");
            var combined = window.BuildPlans(timestamp);
            Check(combined.Count == 1 && combined[0].Sources.SequenceEqual(new[] { landscape, portrait, appendix }), "Combine mode uses visible list only in chosen order");
            Check(window.NoticesPanel.Visibility == Visibility.Collapsed && window.NoticeColumn.Width.Value == 0, "Combine mode expands the main panel and hides append");
            Check(window.NewslettersPanel.Files[^1].IsAppended && window.NewslettersPanel.Files[^1].RowBackground != window.NewslettersPanel.Files[0].RowBackground && window.NewslettersPanel.Files[^1].AppendedVisibility == Visibility.Visible, "Appended files appear at the bottom with a distinct colour and label");
            Capture(window, Path.Combine(folder, "combine.png"));
            var combinedOutputs = await Task.Run(() => new CombineJob(new DocumentConverter(), new PdfSizeOptimizer(new WpfRasterCodec())).RunAsync(combined, Path.Combine(folder, "combined-output")));
            using (var pdf = PdfReader.Open(combinedOutputs.Single(), PdfDocumentOpenMode.Import))
                Check(pdf.PageCount == 3 && pdf.Pages[0].Width.Point > pdf.Pages[0].Height.Point && pdf.Pages[1].Height.Point > pdf.Pages[1].Width.Point && (int)pdf.Pages[2].Width.Point == 321, "Windows combined output includes the visible appended document once");
            window.NewsletterMode.IsChecked = true;
            Check(window.NoticesPanel.Files.Single().Path == appendix && window.NoticesPanel.Visibility == Visibility.Visible, "Returning to newsletters restores the retained notices");
            Check(window.NewslettersPanel.Files[1].Year == "Prep" && window.NewslettersPanel.Files.All(f => f.YearVisibility == Visibility.Visible), "Returning to newsletters restores year controls and edited Prep assignment");
            window.BulkMode.IsChecked = true;
            window.CombineMode.IsChecked = true;
            window.NewslettersPanel.FileList.SelectedIndex = 2;
            window.NewslettersPanel.UpButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(window.BuildPlans(timestamp)[0].Sources.SequenceEqual(new[] { landscape, appendix, portrait }), "Appended documents can be reordered in the combined output");
            window.BulkMode.IsChecked = true;
            Check(window.NewslettersPanel.Files.Select(f => f.Path).SequenceEqual(new[] { landscape, portrait }) && window.NoticesPanel.Files.Single().Path == appendix && !window.NoticesPanel.Files[0].IsAppended, "Returning to bulk moves appended items back without changing main order");
            window.CombineMode.IsChecked = true;
            window.NewslettersPanel.Files.Remove(window.NewslettersPanel.Files.Single(f => f.IsAppended));
            window.BulkMode.IsChecked = true;
            Check(window.NoticesPanel.Files.Count == 0, "Removing an appended item in Combine all does not resurrect it on return");
            window.NoticesPanel.Files.Clear();
            var noAppend = window.BuildPlans(timestamp);
            Check(noAppend.Count == 2 && noAppend.All(p => p.Sources.Count == 1), "Bulk mode works with empty append column");
            var noAppendOutputs = await Task.Run(() => new CombineJob(new DocumentConverter(), new PdfSizeOptimizer(new WpfRasterCodec())).RunAsync(noAppend, Path.Combine(folder, "no-append-output")));
            Check(noAppendOutputs.All(p => { using var pdf = PdfReader.Open(p, PdfDocumentOpenMode.Import); return pdf.PageCount == 1; }), "Windows bulk conversion without append creates standalone PDFs");
            window.SuffixBox.Text = "_{invalid}";
            Check(!window.GoButton.IsEnabled, "Invalid filename variable disables GO");
            window.SuffixBox.Text = "_{n}";
            Check(window.GoButton.IsEnabled, "Valid filename re-enables GO");
            window.NewslettersPanel.Files.Clear();
            Check(!window.GoButton.IsEnabled, "Empty primary file list disables GO");
            window.NewslettersPanel.AddPaths([landscape]);
            window.NewsletterMode.IsChecked = true;
            Check(window.NewslettersPanel.Files[0].Year == "4", "Files added in bulk mode acquire a year suggestion when needed");
            window.WeeklyChoice.IsChecked = true;
            Check(window.LastWeekControls.Visibility == Visibility.Collapsed && window.BuildPlans(timestamp)[0].FileName == "Year 4 Newsletter Term 4 W5.pdf", "Weekly mode creates a single-week filename");
            window.NewslettersPanel.Files[0].Year = "Year 04";
            Check(window.NamingPreview.Text == window.BuildPlans(timestamp)[0].FileName, "Newsletter preview matches normalized output names");
            window.NewslettersPanel.Files[0].Year = "4";
            window.CustomGroupsChoice.IsChecked = true;
            window.GroupNamesBox.Text = "Joeys, Cubs, Scouts";
            Check(!window.GoButton.IsEnabled, "Unmatched custom group requires assignment");
            window.NewslettersPanel.Files[0].Assignment = "Cubs";
            window.CustomIssueChoice.IsChecked = true;
            Check(!window.GoButton.IsEnabled, "Custom mode requires an issue label");
            Capture(window, Path.Combine(folder, "custom-placeholder.png"));
            window.IssueBox.Text = "October 2026";
            var custom = window.BuildPlans(timestamp);
            Check(custom[0].FileName == "Cubs Newsletter October 2026.pdf" && window.GoButton.IsEnabled && window.TermWeekControls.Visibility == Visibility.Collapsed, "Custom group and issue create the requested name without term/week controls");
            Capture(window, Path.Combine(folder, "custom-newsletters.png"));
            var customOutputs = await Task.Run(() => new CombineJob(new DocumentConverter(), new PdfSizeOptimizer(new WpfRasterCodec())).RunAsync(custom, Path.Combine(folder, "custom-output")));
            Check(Path.GetFileName(customOutputs.Single()) == "Cubs Newsletter October 2026.pdf", "Windows custom newsletter is saved with the selected group and issue");
            window.YearGroupsChoice.IsChecked = true;
            Check(window.NewslettersPanel.Files[0].Assignment == "4", "School year assignment survives switching custom groups off");
            window.CustomGroupsChoice.IsChecked = true;
            Check(window.NewslettersPanel.Files[0].Assignment == "Cubs", "Custom group assignment survives switching back");
            var scoutImage = Path.Combine(input, "Scouts camp.png"); File.Copy(landscape, scoutImage);
            window.NewslettersPanel.AddPaths([scoutImage]);
            Check(window.NewslettersPanel.Files[1].Assignment == "Scouts", "New files infer the configured custom group from their filename");
            var seaImage = Path.Combine(input, "Sea Scouts camp.png"); File.Copy(landscape, seaImage);
            window.NewslettersPanel.AddPaths([seaImage]);
            Check(window.NewslettersPanel.Files[^1].Assignment == "Scouts", "Initial group inference uses the configured list");
            window.GroupNamesBox.Text = "Joeys, Cubs, Scouts, Sea Scouts";
            Check(window.NewslettersPanel.Files[^1].Assignment == "Sea Scouts" && window.NewslettersPanel.Files[0].Assignment == "Cubs", "Editing group names updates inferred assignments and retains manual choices");
            window.NewslettersPanel.Files.RemoveAt(window.NewslettersPanel.Files.Count - 1);
            window.NewslettersPanel.Files[1].Assignment = "Joeys";
            window.BulkMode.IsChecked = true; window.NewsletterMode.IsChecked = true;
            Check(window.NewslettersPanel.Files[1].Assignment == "Joeys", "Manual custom assignment survives mode switches");
            window.IssueBox.Text = "October/November";
            Check(!window.GoButton.IsEnabled, "Unsafe custom issue disables GO before processing");
            window.IssueBox.Text = "October 2026";
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -10000;
            window.Show();
            await Task.Delay(350);
            window.CombineMode.IsChecked = true;
            await Task.Delay(100);
            if (SystemParameters.ClientAreaAnimation)
                Check(window.NoticeFraction > 0 && window.NoticeFraction < 1, "Combine transition animates through an intermediate width");
            window.BulkMode.IsChecked = true; // Reverse an in-flight transition.
            await Task.Delay(450);
            Check(Math.Abs(window.NoticeFraction - 1) < 0.001 && window.NoticesPanel.Visibility == Visibility.Visible, "Rapid mode reversal restores both columns");
            window.CombineMode.IsChecked = true;
            await Task.Delay(450);
            window.ArrangeMode.IsChecked = true;
            Check(window.NoticeFraction == 0 && !DependencyPropertyHelper.GetValueSource(window, MainWindow.NoticeFractionProperty).IsAnimated, "Combine all to Arrange pages keeps the full-width layout without animation");
            window.CombineMode.IsChecked = true;
            window.UpdateLayout();
            Check(window.NoticeFraction == 0 && !DependencyPropertyHelper.GetValueSource(window, MainWindow.NoticeFractionProperty).IsAnimated && Math.Abs(window.NewslettersPanel.ActualWidth - window.FileColumns.ActualWidth) < 1, "Arrange pages to Combine all starts fully expanded without animation");
            await Task.Delay(100);
            Check(window.NoticeFraction == 0, "The combined panel remains fully expanded after switching from Arrange pages");
            window.BulkMode.IsChecked = true;
            await Task.Delay(450);
            window.NewsletterMode.IsChecked = true;
            Check(window.NoticeFraction == 1 && !DependencyPropertyHelper.GetValueSource(window, MainWindow.NoticeFractionProperty).IsAnimated, "Switching between two-column modes does not animate");
            window.CombineMode.IsChecked = true;
            await Task.Delay(60);
            window.ArrangeMode.IsChecked = true;
            window.CombineMode.IsChecked = true;
            window.UpdateLayout();
            Check(window.NoticeFraction == 0 && !DependencyPropertyHelper.GetValueSource(window, MainWindow.NoticeFractionProperty).IsAnimated, "Rapid switching through Arrange pages settles an earlier expansion without replaying it");
            window.Width = 1000;
            window.UpdateLayout();
            Check(Math.Abs(window.NewslettersPanel.ActualWidth - window.FileColumns.ActualWidth) < 1 && window.NoticesPanel.Visibility == Visibility.Collapsed, "Expanded panel fills the resized window");
            window.ArrangeMode.IsChecked = true;
            window.NewsletterMode.IsChecked = true;
            await Task.Delay(100);
            if (SystemParameters.ClientAreaAnimation)
                Check(window.NoticeFraction > 0 && window.NoticeFraction < 1, "Arrange pages to newsletters animates the change from one column to two");
            await Task.Delay(350);
            Check(Math.Abs(window.NewslettersPanel.ActualWidth - window.NoticesPanel.ActualWidth) < 1, "Leaving Combine all restores equal panel widths");
            window.Height = window.MinHeight;
            window.UpdateLayout();
            var rootContent = (FrameworkElement)window.Content;
            var goBounds = window.GoButton.TransformToAncestor(rootContent).TransformBounds(new Rect(window.GoButton.RenderSize));
            Check(goBounds.Bottom <= rootContent.ActualHeight + 1, "GO remains inside the minimum-size window");
            Capture(window, Path.Combine(folder, "custom-minimum.png"), 1000, 800);
            var about = window.CreateAboutWindow();
            about.ShowInTaskbar = false; about.WindowStartupLocation = WindowStartupLocation.Manual; about.Left = -10000;
            about.Show(); about.UpdateLayout();
            Capture(about, Path.Combine(folder, "about.png"), 650, 500);
            about.Close();
            File.WriteAllText(Path.Combine(folder, "results.json"), JsonSerializer.Serialize(new { Version = AppInfo.Version, Passed = checks.Count, Checks = checks }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { window.Close(); }
    }

    static void Image(string path, int width, int height)
    {
        var pixels = new byte[width * height * 3];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var i = (y * width + x) * 3;
            var border = x < 12 || x >= width - 12 || y < 12 || y >= height - 12;
            pixels[i] = border ? (byte)168 : (byte)115;
            pixels[i + 1] = border ? (byte)15 : (byte)186;
            pixels[i + 2] = border ? (byte)43 : (byte)240;
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Rgb24, null, pixels, width * 3)));
        using var output = File.Create(path); encoder.Save(output);
    }

    static void Capture(Window window, string path, int width = 1180, int height = 800)
    {
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle((Brush)window.FindResource("Paper"), null, new Rect(0, 0, width, height));
            drawing.DrawRectangle(new VisualBrush(content), null, new Rect(30, 24, content.ActualWidth, content.ActualHeight));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path); encoder.Save(output);
    }
}
