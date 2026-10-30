using NewsletterCombiner.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;

var root = Path.Combine(Path.GetTempPath(), "newsletter-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var tests = new List<(string Name, Func<Task> Run)>();
void Test(string name, Action action) => tests.Add((name, () => { action(); return Task.CompletedTask; }));
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}");
}
async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
{
    try { await action(); } catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}");
}

string Fixture(string name, params int[] widths)
{
    var path = Path.Combine(root, name);
    using var doc = new PdfDocument();
    foreach (var width in widths)
    {
        var page = doc.AddPage();
        page.Width = XUnit.FromPoint(width);
        page.Height = XUnit.FromPoint(700);
        using var g = XGraphics.FromPdfPage(page);
        g.DrawRectangle(new XSolidBrush(XColor.FromArgb(255, 40, 130, 210)), 20, 20, 150, 100);
    }
    doc.Save(path);
    return path;
}

var year3a = Fixture("Year 3 A.pdf", 301, 302);
var year3b = Fixture("Year 3 B.pdf", 303);
var year4 = Fixture("Year 4.pdf", 401);
var notice1 = Fixture("Notice 1.pdf", 501);
var notice2 = Fixture("Notice 2.pdf", 502);

Test("Year inference handles filenames, folders and combined classes", () =>
{
    Check(NewsletterPlan.GuessYear("Year 3 Newsletter W5-6.pdf") == "3", "year 3");
    Check(NewsletterPlan.GuessYear("/school/Year 4/newsletter.docx") == "4", "folder year");
    Check(NewsletterPlan.GuessYear("Yr_5-6 newsletter.pdf") == "5-6", "combined class");
    Check(NewsletterPlan.GuessYear("Prep_newsletter.pdf") == "Prep", "Prep");
    Check(NewsletterPlan.GuessYear("newsletter W5-6.pdf") == "", "must not guess from weeks");
    Check(NewsletterPlan.GuessYear("Year 13.pdf") == "", "invalid year");
});
Test("Year normalization prevents unsafe output names", () =>
{
    Check(NewsletterPlan.NormalizeYear(" Year 03 ") == "3", "normalize");
    Check(NewsletterPlan.NormalizeYear("5/6") == "5-6", "group");
    Throws<ArgumentException>(() => NewsletterPlan.NormalizeYear("../../school"));
    Throws<ArgumentException>(() => NewsletterPlan.NormalizeYear(""));
});
Test("Each year gets its own newsletters followed by all notices", () =>
{
    var plans = NewsletterPlan.Create([new(year3a, "3"), new(year4, "4"), new(year3b, "Year 3")], [notice2, notice1], 2, 5, 6);
    Check(plans.Count == 2, "output count");
    Check(plans[0].FileName == "Year 3 Newsletter Term 2 W5-6.pdf", "filename");
    Check(plans[0].Sources.SequenceEqual(new[] { year3a, year3b, notice2, notice1 }), "order");
    Check(plans[1].Sources.SequenceEqual(new[] { year4, notice2, notice1 }), "second year");
});
Test("Invalid terms, reversed weeks, duplicates and missing sources are rejected", () =>
{
    Throws<ArgumentException>(() => NewsletterPlan.Create([new(year3a, "3")], [], 5, 5, 6));
    Throws<ArgumentException>(() => NewsletterPlan.Create([new(year3a, "3")], [], 1, 6, 5));
    Throws<ArgumentException>(() => NewsletterPlan.Create([new(year3a, "3")], [year3a], 1, 5, 6));
    Throws<FileNotFoundException>(() => NewsletterPlan.Create([new(Path.Combine(root, "missing.pdf"), "3")], [], 1, 5, 6));
    Throws<ArgumentException>(() => NewsletterPlan.Create([], [], 1, 5, 6));
});
Test("Prep filenames omit Year while numbered years retain it", () =>
{
    var plans = NewsletterPlan.Create([new(year3a, "Year Prep"), new(year4, "4")], [notice1], 2, 5, 6);
    Check(plans[0].FileName == "Prep Newsletter Term 2 W5-6.pdf", "Prep prefix");
    Check(plans[1].FileName == "Year 4 Newsletter Term 2 W5-6.pdf", "numbered year prefix");
    Check(NewsletterPlan.OutputFileName("prep", 2, 5, 6) == plans[0].FileName, "preview differs from saved filename");
});
Test("Merged page sizes, order and colour drawing commands are preserved", () =>
{
    var path = Path.Combine(root, "merge.pdf");
    PdfCombiner.Merge([year3a, year3b, notice2, notice1], path, "Test newsletter");
    using var output = PdfReader.Open(path, PdfDocumentOpenMode.Import);
    Check(output.PageCount == 5, "page count");
    Check(Enumerable.Range(0, 5).Select(i => (int)output.Pages[i].Width.Point).SequenceEqual(new[] { 301, 302, 303, 502, 501 }), "page order and size");
    Check(output.Info.Title == "Test newsletter", "metadata");
    using var original = PdfReader.Open(year3a, PdfDocumentOpenMode.Import);
    var before = original.Pages[0].Contents.CreateSingleContent().Stream.UnfilteredValue;
    var after = output.Pages[0].Contents.CreateSingleContent().Stream.UnfilteredValue;
    Check(before.SequenceEqual(after), "colour/page content changed");
});

string LinksFixture()
{
    var path = Path.Combine(root, "links.pdf");
    using var doc = new PdfDocument();
    var page1 = doc.AddPage();
    var page2 = doc.AddPage();
    page1.AddWebLink(new PdfRectangle(new XPoint(10, 10), new XPoint(100, 30)), "https://example.org/news?a=1&b=2#details");
    page1.AddWebLink(new PdfRectangle(new XPoint(10, 40), new XPoint(100, 60)), "mailto:school@example.org");
    var destination = new PdfArray(doc);
    destination.Elements.Add(page2.Reference!);
    destination.Elements.Add(new PdfName("/Fit"));
    var direct = new PdfDictionary(doc);
    direct.Elements.SetName("/Subtype", "/Link");
    direct.Elements.SetRectangle("/Rect", new PdfRectangle(new XPoint(10, 70), new XPoint(100, 90)));
    direct.Elements["/Dest"] = destination;
    doc.Internals.AddObject(direct);
    page1.Elements.GetArray("/Annots")!.Elements.Add(direct.Reference!);

    var dests = new PdfDictionary(doc);
    var namePairs = new PdfArray(doc);
    namePairs.Elements.Add(new PdfString("chapter")); namePairs.Elements.Add(destination);
    dests.Elements["/Names"] = namePairs;
    var names = new PdfDictionary(doc); names.Elements["/Dests"] = dests;
    doc.Internals.Catalog.Elements["/Names"] = names;
    var named = new PdfDictionary(doc);
    named.Elements.SetName("/Subtype", "/Link");
    named.Elements.SetRectangle("/Rect", new PdfRectangle(new XPoint(10, 100), new XPoint(100, 120)));
    var action = new PdfDictionary(doc); action.Elements.SetName("/S", "/GoTo"); action.Elements["/D"] = new PdfString("chapter");
    named.Elements["/A"] = action;
    doc.Internals.AddObject(named);
    page1.Elements.GetArray("/Annots")!.Elements.Add(named.Reference!);
    var oldDests = new PdfDictionary(doc); oldDests.Elements["/legacy"] = destination;
    doc.Internals.Catalog.Elements["/Dests"] = oldDests;
    var legacyLink = new PdfDictionary(doc);
    legacyLink.Elements.SetName("/Subtype", "/Link");
    legacyLink.Elements.SetRectangle("/Rect", new PdfRectangle(new XPoint(10, 130), new XPoint(100, 150)));
    legacyLink.Elements["/Dest"] = new PdfName("/legacy");
    doc.Internals.AddObject(legacyLink);
    page1.Elements.GetArray("/Annots")!.Elements.Add(legacyLink.Reference!);
    doc.Save(path);
    return path;
}
var links = LinksFixture();
Test("Web and email links survive with their exact destinations", () =>
{
    var path = Path.Combine(root, "hyperlinks.pdf");
    PdfCombiner.Merge([notice1, links, notice2], path, "Links");
    using var output = PdfReader.Open(path, PdfDocumentOpenMode.Import);
    var annotations = output.Pages[1].Elements.GetArray("/Annots")!;
    Check(annotations.Elements.Count == 5, "lost annotations");
    Check(annotations.Elements.GetDictionary(0)!.Elements.GetDictionary("/A")!.Elements.GetString("/URI") == "https://example.org/news?a=1&b=2#details", "web URL");
    Check(annotations.Elements.GetDictionary(1)!.Elements.GetDictionary("/A")!.Elements.GetString("/URI") == "mailto:school@example.org", "email URL");
});
Test("Direct, named and legacy internal links point to the correct inserted page", () =>
{
    using var output = PdfReader.Open(Path.Combine(root, "hyperlinks.pdf"), PdfDocumentOpenMode.Import);
    var annotations = output.Pages[1].Elements.GetArray("/Annots")!;
    var direct = annotations.Elements.GetDictionary(2)!.Elements.GetArray("/Dest")!;
    var named = annotations.Elements.GetDictionary(3)!.Elements.GetDictionary("/A")!.Elements.GetArray("/D")!;
    var legacy = annotations.Elements.GetDictionary(4)!.Elements.GetArray("/Dest")!;
    foreach (var destination in new[] { direct, named, legacy })
        Check(((PdfReference)destination.Elements[0]).ObjectID == output.Pages[2].Reference!.ObjectID, "internal link is broken after merge");
});
Test("PNG becomes a proportionate A4 page", () =>
{
    var png = Path.Combine(root, "notice.png");
    File.WriteAllBytes(png, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAAEUlEQVR4nGNgSDnx/3+FUQMAErgEVY+1mRAAAAAASUVORK5CYII="));
    var pdf = Path.Combine(root, "image.pdf");
    PdfCombiner.ImageToPdf(png, pdf);
    using var output = PdfReader.Open(pdf, PdfDocumentOpenMode.Import);
    Check(output.PageCount == 1, "image pages");
    Check(Math.Abs(output.Pages[0].Width.Point - 842) < 1, "A4 landscape width");
    Check(output.Pages[0].Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/XObject")!.Elements.Count > 0, "missing image");
});
foreach (var (name, width, height) in new[] { ("portrait", 200, 400), ("landscape", 400, 200), ("panorama", 800, 80), ("tall", 80, 800), ("square", 200, 200) })
{
    Test($"A4 {name} image keeps orientation, proportions, full resolution and all four edges", () =>
    {
        var path = Path.Combine(root, name + "-A4.pdf");
        PdfCombiner.ImageToPdf(Path.Combine(AppContext.BaseDirectory, "fixtures", name + ".png"), path);
        using var output = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        var page = output.Pages[0];
        var landscape = width > height;
        Check(Math.Abs(page.Width.Point - (landscape ? 842 : 595)) < 1, "A4 width/orientation");
        Check(Math.Abs(page.Height.Point - (landscape ? 595 : 842)) < 1, "A4 height/orientation");
        var content = Encoding.ASCII.GetString(page.Contents.CreateSingleContent().Stream.UnfilteredValue);
        // Inspect the actual image placement written into the PDF, not just the layout calculation.
        var matrix = Regex.Match(content, @"q\s+([\d.]+) 0 0 ([\d.]+) ([\d.\-]+) ([\d.\-]+) cm /\w+ Do");
        Check(matrix.Success, "image placement missing");
        double Number(int group) => double.Parse(matrix.Groups[group].Value, CultureInfo.InvariantCulture);
        var w = Number(1); var h = Number(2); var x = Number(3); var y = Number(4);
        Check(x >= -0.01 && y >= -0.01 && x + w <= page.Width.Point + 0.01 && y + h <= page.Height.Point + 0.01, "image extends beyond the page");
        Check(Math.Abs(w / h - (double)width / height) < 0.001, "image stretched");
        Check(Math.Abs((page.Width.Point - w) / 2 - x) < 0.01 && Math.Abs((page.Height.Point - h) / 2 - y) < 0.01, "image not centred");
        Check(Math.Abs(w - page.Width.Point) < 0.01 || Math.Abs(h - page.Height.Point) < 0.01, "image not fitted to A4");
        Check(!Regex.IsMatch(content, @"\bW\*?\s"), "unexpected crop/clipping command");
        var images = page.Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/XObject")!;
        var image = images.Elements.GetDictionary(images.Elements.Keys.Single())!;
        Check(image.Elements.GetInteger("/Width") == width && image.Elements.GetInteger("/Height") == height, "source pixels were resampled");
    });
}
tests.Add(("Batch outputs share notices and keep originals intact", async () =>
{
    var output = Path.Combine(root, "batch");
    var original = File.ReadAllBytes(year3a);
    var plans = NewsletterPlan.Create([new(year3a, "3"), new(year4, "4")], [notice1], 3, 7, 8);
    var result = await new CombineJob(new FakeConverter()).RunAsync(plans, output);
    Check(result.Count == 2, "batch size");
    using var first = PdfReader.Open(result[0], PdfDocumentOpenMode.Import);
    using var second = PdfReader.Open(result[1], PdfDocumentOpenMode.Import);
    Check(first.PageCount == 3 && second.PageCount == 2, "batch page counts");
    Check((int)first.Pages[2].Width.Point == 501 && (int)second.Pages[1].Width.Point == 501, "shared notice missing");
    Check(original.SequenceEqual(File.ReadAllBytes(year3a)), "original changed");
    Check(!Directory.EnumerateDirectories(output).Any(), "temporary files left behind");
}));
tests.Add(("Conversion failure produces no partial batch", async () =>
{
    var output = Path.Combine(root, "failure");
    var corrupt = Path.Combine(root, "corrupt.pdf"); File.WriteAllText(corrupt, "not a pdf");
    var plans = NewsletterPlan.Create([new(year3a, "3"), new(corrupt, "4")], [notice1], 1, 1, 2);
    await ThrowsAsync<InvalidDataException>(() => new CombineJob(new FakeConverter()).RunAsync(plans, output));
    Check(!Directory.EnumerateFileSystemEntries(output).Any(), "partial output or temporary files");
}));
tests.Add(("Existing PDFs are never overwritten", async () =>
{
    var output = Path.Combine(root, "existing"); Directory.CreateDirectory(output);
    var plans = NewsletterPlan.Create([new(year3a, "3")], [], 1, 1, 2);
    var existing = Path.Combine(output, plans[0].FileName); File.WriteAllText(existing, "keep me");
    await ThrowsAsync<IOException>(() => new CombineJob(new FakeConverter()).RunAsync(plans, output));
    Check(File.ReadAllText(existing) == "keep me", "overwritten");
}));
tests.Add(("Cancellation publishes nothing and removes temporary files", async () =>
{
    var output = Path.Combine(root, "cancel");
    var plans = NewsletterPlan.Create([new(year3a, "3")], [notice1], 1, 1, 2);
    using var cancel = new CancellationTokenSource(); cancel.Cancel();
    await ThrowsAsync<OperationCanceledException>(() => new CombineJob(new FakeConverter()).RunAsync(plans, output, token: cancel.Token));
    Check(!Directory.EnumerateFileSystemEntries(output).Any(), "cancel left files");
}));
tests.Add(("Shared Office notice is converted only once per batch", async () =>
{
    var source = Path.Combine(root, "notice.docx"); File.WriteAllText(source, "fake Office input");
    var plans = NewsletterPlan.Create([new(year3a, "3"), new(year4, "4")], [source], 1, 1, 2);
    var converter = new FakeConverter(notice1);
    await new CombineJob(converter).RunAsync(plans, Path.Combine(root, "cached"));
    Check(converter.Count == 1, "notice converted more than once");
}));
Test("Protected PDFs fail clearly without modifying the original", () =>
{
    var path = Path.Combine(root, "protected.pdf");
    using (var pdf = new PdfDocument()) { pdf.AddPage(); pdf.SecuritySettings.UserPassword = "test-secret"; pdf.Save(path); }
    Throws<InvalidDataException>(() => PdfCombiner.Merge([path], Path.Combine(root, "no-output.pdf"), "Locked"));
    Check(!File.Exists(Path.Combine(root, "no-output.pdf")), "unexpected output");
});

Test("Lossless compression preserves external and internal hyperlinks", () =>
{
    var path = Path.Combine(root, "compress-links.pdf");
    PdfCombiner.Merge([notice1, links], path, "Compression links");
    var before = new FileInfo(path).Length;
    var result = new PdfSizeOptimizer().Optimize(path);
    Check(result.FinalBytes <= before, "compression enlarged the PDF");
    using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
    Check(pdf.PageCount == 3, "pages lost");
    var annots = pdf.Pages[1].Elements.GetArray("/Annots")!;
    Check(annots.Elements.Count == 5, "links lost");
    Check(annots.Elements.GetDictionary(0)!.Elements.GetDictionary("/A")!.Elements.GetString("/URI") == "https://example.org/news?a=1&b=2#details", "URL changed");
    Check(((PdfReference)annots.Elements.GetDictionary(2)!.Elements.GetArray("/Dest")!.Elements[0]).ObjectID == pdf.Pages[2].Reference!.ObjectID, "page link changed");
});
Test("PDFs below 10 MB never enter lossy compression", () =>
{
    var path = Path.Combine(root, "small.pdf"); File.Copy(year3a, path);
    var codec = new RecordingCodec();
    var result = new PdfSizeOptimizer(codec).Optimize(path);
    Check(codec.Profiles.Count == 0 && !result.OverTarget, "small PDF image quality was reduced");
});
Test("Cancelled compression leaves its input untouched", () =>
{
    var path = Path.Combine(root, "cancel-compression.pdf"); File.Copy(year3a, path);
    var original = File.ReadAllBytes(path);
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    Throws<OperationCanceledException>(() => new PdfSizeOptimizer().Optimize(path, cancelled.Token));
    Check(original.SequenceEqual(File.ReadAllBytes(path)), "cancelled compression changed the input");
    Check(!Directory.EnumerateDirectories(root, "cancel-compression.pdf.compression-*").Any(), "temporary compression files remain");
});
Test("Oversized images are sized for their largest page placement, with a bounded quality floor", () =>
{
    var path = Path.Combine(root, "oversized-placement.pdf");
    using (var pdf = new PdfDocument())
    {
        var page = pdf.AddPage();
        var image = new PdfDictionary(pdf);
        image.Elements.SetName("/Type", "/XObject"); image.Elements.SetName("/Subtype", "/Image");
        image.Elements.SetName("/ColorSpace", "/DeviceRGB"); image.Elements.SetInteger("/BitsPerComponent", 8);
        image.Elements.SetInteger("/Width", 2000); image.Elements.SetInteger("/Height", 2000);
        var pixels = new byte[12_000_000]; new Random(42).NextBytes(pixels); image.CreateStream(pixels);
        pdf.Internals.AddObject(image);
        var objects = new PdfDictionary(pdf); objects.Elements["/I0"] = image.Reference!;
        var resources = new PdfDictionary(pdf); resources.Elements["/XObject"] = objects; page.Elements["/Resources"] = resources;
        page.AddWebLink(new PdfRectangle(new XPoint(10, 10), new XPoint(100, 30)), "https://example.org/newsletter?term=4&week=5");
        page.AddWebLink(new PdfRectangle(new XPoint(10, 40), new XPoint(100, 60)), "mailto:newsletter@example.org");
        var content = new PdfDictionary(pdf);
        content.CreateStream(Encoding.ASCII.GetBytes("q 72 0 0 72 0 0 cm /I0 Do Q\nq 144 0 0 144 200 200 cm /I0 Do Q"));
        pdf.Internals.AddObject(content); page.Elements["/Contents"] = content.Reference!;
        pdf.Save(path);
    }
    var codec = new RecordingCodec();
    var result = new PdfSizeOptimizer(codec).Optimize(path);
    Check(result.OverTarget, "oversized result was not flagged");
    Check(result.FinalBytes <= result.OriginalBytes, "output enlarged");
    Check(codec.Profiles.Count == 4, "later compression passes were skipped");
    Check(codec.Profiles[0].LongEdge == 400, "image not sized to its largest rendered placement at 200 dpi");
    Check(codec.Profiles[^1].LongEdge == 300 && codec.Profiles[^1].Quality == 76, "quality floor not respected");
});

var namingTime = new DateTime(2026, 10, 30, 14, 5, 0);
Test("Bulk mode creates one PDF per source without notices or year labels", () =>
{
    var plans = DocumentPlan.CreateBulk([year3a, year4], [], "", namingTime);
    Check(plans.Count == 2, "bulk count");
    Check(plans[0].FileName == "Year 3 A.pdf" && plans[1].FileName == "Year 4.pdf", "original names not retained");
    Check(plans[0].Sources.SequenceEqual(new[] { year3a }), "unexpected append");
});
Test("Bulk mode appends every optional file to each output in order", () =>
{
    var plans = DocumentPlan.CreateBulk([year4, year3a], [notice2, notice1], "_ready", namingTime);
    Check(plans[0].Sources.SequenceEqual(new[] { year4, notice2, notice1 }), "first output order");
    Check(plans[1].Sources.SequenceEqual(new[] { year3a, notice2, notice1 }), "second output order");
    Check(plans[0].FileName == "Year 4_ready.pdf", "literal suffix");
});
Test("Filename variables share the job timestamp and follow displayed file order", () =>
{
    var plans = DocumentPlan.CreateBulk([year4, year3a], [], " - {DATE}_{time}_{n}", namingTime);
    Check(plans[0].FileName == "Year 4 - 2026-10-30_14-05_001.pdf", "first expanded name");
    Check(plans[1].FileName == "Year 3 A - 2026-10-30_14-05_002.pdf", "second expanded name");
});
Test("Combine mode preserves displayed order and appends extras once", () =>
{
    var plans = DocumentPlan.CreateCombined([year4, year3b, year3a], [notice2, notice1], "School pack {date}.PDF", namingTime);
    Check(plans.Count == 1 && plans[0].FileName == "School pack 2026-10-30.pdf", "combined filename");
    Check(plans[0].Sources.SequenceEqual(new[] { year4, year3b, year3a, notice2, notice1 }), "combined order");
    Check(DocumentPlan.CreateCombined([year3a], [], "Single", namingTime)[0].Sources.Count == 1, "single source without append");
});
Test("Bulk name collisions are resolved without taking another source's natural name", () =>
{
    var one = Path.Combine(root, "same-name-one"); var two = Path.Combine(root, "same-name-two");
    Directory.CreateDirectory(one); Directory.CreateDirectory(two);
    var a = Path.Combine(one, "Report.pdf"); var b = Path.Combine(two, "report.pdf"); var c = Path.Combine(one, "report (2).pdf");
    File.Copy(year3a, a); File.Copy(year4, b); File.Copy(notice1, c);
    var plans = DocumentPlan.CreateBulk([a, b, c], [], "", namingTime);
    Check(plans.Select(p => p.FileName).SequenceEqual(new[] { "Report.pdf", "report (3).pdf", "report (2).pdf" }), "collision numbering");
});
Test("Invalid variables, unsafe paths and reserved Windows output names are rejected", () =>
{
    foreach (var name in new[] { "", " ", "../outside", "C:\\output", "CON", "aux.pdf", "LPT1.notes", "bad?name", "trailing.", "{unknown}", "{date", new string('x', 201) })
        Throws<ArgumentException>(() => DocumentPlan.CombinedFileName(name, namingTime));
    Throws<ArgumentException>(() => DocumentPlan.BulkFileName(year3a, "_{year}", namingTime, 1));
    Check(DocumentPlan.CombinedFileName("Report {n}", namingTime) == "Report 001.pdf", "combined counter");
    Check(DocumentPlan.BulkFileName("Literal {date}.docx", "_{n}", namingTime, 1) == "Literal {date}_001.pdf", "source name was treated as a template");
});
Test("New modes reject duplicate, missing and unsupported inputs before conversion", () =>
{
    Throws<ArgumentException>(() => DocumentPlan.CreateBulk([], [notice1], "", namingTime));
    Throws<ArgumentException>(() => DocumentPlan.CreateBulk([year3a], [year3a], "", namingTime));
    Throws<ArgumentException>(() => DocumentPlan.CreateCombined([year3a, year3a], [], "Pack", namingTime));
    Throws<FileNotFoundException>(() => DocumentPlan.CreateBulk([Path.Combine(root, "absent.docx")], [], "", namingTime));
    var unsupported = Path.Combine(root, "unsupported.txt"); File.WriteAllText(unsupported, "Unsupported");
    Throws<ArgumentException>(() => DocumentPlan.CreateCombined([unsupported], [], "Pack", namingTime));
});
tests.Add(("Bulk conversion without append runs conversion and compression and keeps the original", async () =>
{
    var source = Path.Combine(root, "Meeting notes.docx"); File.WriteAllText(source, "fake Office input");
    var plans = DocumentPlan.CreateBulk([source], [], "_{date}", namingTime);
    var converter = new FakeConverter(year3a); var progress = new CollectedProgress();
    var outputs = await new CombineJob(converter, new PdfSizeOptimizer()).RunAsync(plans, Path.Combine(root, "bulk-conversion"), progress);
    Check(converter.Count == 1 && outputs.Count == 1, "conversion not run");
    Check(Path.GetFileName(outputs[0]) == "Meeting notes_2026-10-30.pdf", "published name");
    Check(progress.Messages.Any(s => s.Contains("Compressing PDF")), "compression skipped");
    using var pdf = PdfReader.Open(outputs[0], PdfDocumentOpenMode.Import);
    Check(pdf.PageCount == 2, "pages changed");
    Check(File.ReadAllText(source) == "fake Office input", "original changed");
}));
tests.Add(("Bulk outputs share converted appended files and retain their ending page order", async () =>
{
    var append = Path.Combine(root, "Shared ending.docx"); File.WriteAllText(append, "fake Office input");
    var plans = DocumentPlan.CreateBulk([year4, year3b], [append, notice2], "", namingTime);
    var converter = new FakeConverter(notice1);
    var outputs = await new CombineJob(converter, new PdfSizeOptimizer()).RunAsync(plans, Path.Combine(root, "bulk-append"));
    Check(converter.Count == 1 && outputs.Count == 2, "shared append conversion count");
    foreach (var output in outputs)
    {
        using var pdf = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        Check(pdf.PageCount == 3 && (int)pdf.Pages[1].Width.Point == 501 && (int)pdf.Pages[2].Width.Point == 502, "append page order");
    }
}));
tests.Add(("Combine job creates one PDF in chosen order and preserves links", async () =>
{
    var plans = DocumentPlan.CreateCombined([year4, links, year3b], [notice2], "Combined {date}", namingTime);
    var outputs = await new CombineJob(new FakeConverter(), new PdfSizeOptimizer()).RunAsync(plans, Path.Combine(root, "combine-all"));
    Check(outputs.Count == 1, "not a single document");
    using var pdf = PdfReader.Open(outputs[0], PdfDocumentOpenMode.Import);
    Check(pdf.PageCount == 5 && (int)pdf.Pages[0].Width.Point == 401 && (int)pdf.Pages[3].Width.Point == 303 && (int)pdf.Pages[4].Width.Point == 502, "combined page order");
    var annots = pdf.Pages[1].Elements.GetArray("/Annots")!;
    Check(annots.Elements.Count == 5, "links lost");
    Check(((PdfReference)annots.Elements.GetDictionary(2)!.Elements.GetArray("/Dest")!.Elements[0]).ObjectID == pdf.Pages[2].Reference!.ObjectID, "internal destination changed");
}));
tests.Add(("Bulk mode never overwrites existing outputs and publishes no partial batch", async () =>
{
    var output = Path.Combine(root, "bulk-existing"); Directory.CreateDirectory(output);
    var plans = DocumentPlan.CreateBulk([year4, year3b], [], "", namingTime);
    var existing = Path.Combine(output, plans[1].FileName); File.WriteAllText(existing, "keep me");
    await ThrowsAsync<IOException>(() => new CombineJob(new FakeConverter()).RunAsync(plans, output));
    Check(File.ReadAllText(existing) == "keep me" && Directory.GetFiles(output).Length == 1, "existing output or batch changed");
}));

Test("Weekly naming uses a single week and preserves school prefixes", () =>
{
    var weekly = new NewsletterOptions(NewsletterFrequency.Weekly);
    var plans = NewsletterPlan.Create([new(year3a, "3"), new(year4, "Prep")], [notice1], 2, 5, 2, weekly);
    Check(plans[0].FileName == "Year 3 Newsletter Term 2 W5.pdf", "weekly year name");
    Check(plans[1].FileName == "Prep Newsletter Term 2 W5.pdf", "weekly Prep name");
    Check(plans[0].Sources.SequenceEqual(new[] { year3a, notice1 }), "weekly notice order");
});
Test("Custom issues work without term/week values and keep group order", () =>
{
    var options = new NewsletterOptions(NewsletterFrequency.Custom, true, "October 2026");
    var plans = NewsletterPlan.Create([new(year3a, " Cubs "), new(year4, "Scouts"), new(year3b, "cubs")], [notice1], 0, 0, 0, options);
    Check(plans.Count == 2 && plans[0].FileName == "Cubs Newsletter October 2026.pdf", "custom output name");
    Check(plans[0].Sources.SequenceEqual(new[] { year3a, year3b, notice1 }), "custom grouping or notice order");
    Check(NewsletterPlan.OutputFileName("3", 0, 0, 0, new(NewsletterFrequency.Custom, false, "Camp update")) == "Year 3 Newsletter Camp update.pdf", "school custom issue");
});
Test("Custom names reject invalid Windows names and empty issue labels", () =>
{
    foreach (var invalid in new[] { "../Cubs", "Cubs/Scouts", "CON", "Cubs:", "", "Cubs." })
        Throws<ArgumentException>(() => NewsletterPlan.NormalizeGroup(invalid));
    foreach (var invalid in new[] { "", "October/November", new string('a', 200) })
        Throws<ArgumentException>(() => NewsletterPlan.OutputFileName("Cubs", 1, 1, 2, new(NewsletterFrequency.Custom, true, invalid)));
});
Test("Custom group matching prefers filenames and specific whole names", () =>
{
    string[] groups = ["Joeys", "Cubs", "Scouts", "Sea Scouts"];
    Check(NewsletterPlan.GuessGroup("/Scouts/CUBS_newsletter.pdf", groups) == "Cubs", "filename priority and case");
    Check(NewsletterPlan.GuessGroup("/Joeys/newsletter.pdf", groups) == "Joeys", "folder inference");
    Check(NewsletterPlan.GuessGroup("Sea-Scouts update.pdf", groups) == "Sea Scouts", "longest specific name");
    Check(NewsletterPlan.GuessGroup("Cubsville.pdf", groups) == "", "partial name must not match");
    Check(NewsletterPlan.GuessGroup("/Scouts/Joeys and Cubs.pdf", groups) == "", "ambiguous name must need user choice");
});
Test("Custom group list removes duplicates and preserves user spelling", () =>
{
    Check(NewsletterPlan.ParseGroups(" Joeys, Cubs; Scouts\nCUBS, Sea   Scouts ").SequenceEqual(new[] { "Joeys", "Cubs", "Scouts", "Sea Scouts" }), "parsed list");
    Check(NewsletterPlan.ParseGroups("").Length == 0, "empty list for manual assignment");
    Throws<ArgumentException>(() => NewsletterPlan.ParseGroups("Cubs, Scouts/Joeys"));
});
Test("Custom group newsletters also support fortnightly and weekly filenames", () =>
{
    Check(NewsletterPlan.OutputFileName("Cubs", 2, 5, 6, new(NewsletterFrequency.Fortnightly, true)) == "Cubs Newsletter Term 2 W5-6.pdf", "fortnight name");
    Check(NewsletterPlan.OutputFileName("Joeys", 2, 5, 6, new(NewsletterFrequency.Weekly, true)) == "Joeys Newsletter Term 2 W5.pdf", "weekly name");
});

Test("Arranging pages preserves vectors, dimensions and source bytes with duplicates", () =>
{
    var before = File.ReadAllBytes(year3a);
    var output = Path.Combine(root, "arranged-duplicates.pdf");
    PdfCombiner.Arrange(year3a, [1, 0, 1], output, "Arranged");
    using var pdf = PdfReader.Open(output, PdfDocumentOpenMode.Import);
    using var original = PdfReader.Open(year3a, PdfDocumentOpenMode.Import);
    Check(pdf.PageCount == 3 && pdf.Pages.Cast<PdfPage>().Select(p => (int)p.Width.Point).SequenceEqual(new[] { 302, 301, 302 }), "wrong order or duplicate dimensions");
    Check(pdf.Pages[0].Reference!.ObjectID != pdf.Pages[2].Reference!.ObjectID, "duplicate must have a distinct page object");
    Check(pdf.Pages[0].Contents.CreateSingleContent().Stream.UnfilteredValue.SequenceEqual(original.Pages[1].Contents.CreateSingleContent().Stream.UnfilteredValue), "page was flattened or changed");
    Check(File.ReadAllBytes(year3a).SequenceEqual(before), "original changed");
});
Test("Reordered and duplicated pages retain external and remapped internal links", () =>
{
    var output = Path.Combine(root, "arranged-links.pdf");
    PdfCombiner.Arrange(links, [1, 0, 0], output, "Links");
    using var pdf = PdfReader.Open(output, PdfDocumentOpenMode.Import);
    for (var i = 1; i < 3; i++)
    {
        var annots = pdf.Pages[i].Elements.GetArray("/Annots")!;
        Check(annots.Elements.Count == 5, "lost links in duplicate");
        Check(annots.Elements.GetDictionary(0)!.Elements.GetDictionary("/A")!.Elements.GetString("/URI") == "https://example.org/news?a=1&b=2#details", "web link changed");
        Check(annots.Elements.GetDictionary(1)!.Elements.GetDictionary("/A")!.Elements.GetString("/URI") == "mailto:school@example.org", "email changed");
        foreach (var j in new[] { 2, 3, 4 })
        {
            var annotation = annots.Elements.GetDictionary(j)!;
            var dest = j == 3 ? annotation.Elements.GetDictionary("/A")!.Elements.GetArray("/D")! : annotation.Elements.GetArray("/Dest")!;
            Check(((PdfReference)dest.Elements[0]).ObjectID == pdf.Pages[0].Reference!.ObjectID, "internal link not remapped");
            Check(((PdfReference)annotation.Elements["/P"]!).ObjectID == pdf.Pages[i].Reference!.ObjectID, "annotation belongs to wrong duplicate");
        }
    }
});
Test("Links to removed pages are omitted while web links remain", () =>
{
    var output = Path.Combine(root, "arranged-removed.pdf");
    PdfCombiner.Arrange(links, [0, 0], output, "Removed");
    using var pdf = PdfReader.Open(output, PdfDocumentOpenMode.Import);
    Check(pdf.PageCount == 2, "removed page included");
    Check(pdf.Pages.Cast<PdfPage>().All(p => p.Elements.GetArray("/Annots")!.Elements.Count == 2), "dangling internal links retained");
});
Test("Page arrangement rejects empty selections and invalid page indices", () =>
{
    var output = Path.Combine(root, "invalid-arranged.pdf");
    Throws<ArgumentException>(() => PdfCombiner.Arrange(links, [], output, "Invalid"));
    Throws<ArgumentException>(() => PdfCombiner.Arrange(links, [99], output, "Invalid"));
    Check(!File.Exists(output), "failed arrangement created output");
});
Test("Arranged saving never overwrites and cancelled saves leave no output", () =>
{
    var folder = Path.Combine(root, "arrange-save"); Directory.CreateDirectory(folder);
    var destination = Path.Combine(folder, "existing.pdf"); File.WriteAllText(destination, "keep");
    var job = new PageArrangementJob();
    Throws<IOException>(() => job.Save(links, [0], folder, "existing.pdf"));
    using var cancel = new CancellationTokenSource(); cancel.Cancel();
    Throws<OperationCanceledException>(() => job.Save(links, [0], folder, "cancelled.pdf", cancel.Token));
    Check(File.ReadAllText(destination) == "keep" && Directory.GetFileSystemEntries(folder).Length == 1, "unsafe or partial save");
    var saved = job.Save(year3a, [1], folder, "selected.pdf");
    using var pdf = PdfReader.Open(saved, PdfDocumentOpenMode.Import);
    Check(pdf.PageCount == 1 && (int)pdf.Pages[0].Width.Point == 302, "save order");
});

tests.Add(("Printing 10,000 outputs stays sequential and waits for capacity before each submission", async () =>
{
    var paths = Enumerable.Range(0, 10_000).Select(i => $"document-{i:00000}.pdf").ToArray();
    var spooler = new RecordingSpooler();
    var result = await PrintBatch.RunAsync(paths, spooler);
    Check(result.Submitted == paths.Length && result.Error == null && !result.Cancelled, "batch result");
    Check(spooler.Paths.SequenceEqual(paths) && spooler.CapacityChecks == paths.Length && spooler.MaxConcurrent == 1, "order or concurrency");
}));
tests.Add(("Print failure stops without retrying or altering saved PDFs", async () =>
{
    var bytes = File.ReadAllBytes(year3a);
    var spooler = new RecordingSpooler { FailAt = 1 };
    var result = await PrintBatch.RunAsync(new[] { year3a, year4, notice1 }, spooler);
    Check(result.Submitted == 1 && result.Error!.Contains(Path.GetFileName(year4)) && spooler.Paths.Count == 1, "failed job was retried or later files printed");
    Check(File.ReadAllBytes(year3a).SequenceEqual(bytes), "saved output changed");
}));
tests.Add(("Cancelling while the print queue is full submits no further files", async () =>
{
    using var cancel = new CancellationTokenSource();
    var spooler = new RecordingSpooler { CapacityAction = n => { if (n == 2) cancel.Cancel(); } };
    var result = await PrintBatch.RunAsync(new[] { year3a, year4, notice1 }, spooler, token: cancel.Token);
    Check(result.Cancelled && result.Submitted == 1 && spooler.Paths.Count == 1 && File.Exists(year4), "cancel did not retain outputs or stop submission");
}));
tests.Add(("Cancelled print batches do not contact the spooler", async () =>
{
    using var cancel = new CancellationTokenSource(); cancel.Cancel();
    var spooler = new RecordingSpooler();
    var result = await PrintBatch.RunAsync(new[] { year3a }, spooler, token: cancel.Token);
    Check(result.Cancelled && result.Submitted == 0 && spooler.CapacityChecks == 0, "cancelled job started");
}));

var failures = 0;
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception e) { failures++; Console.WriteLine($"FAIL {test.Name}: {e}"); }
}
Console.WriteLine($"{tests.Count - failures}/{tests.Count} passed");
if (Environment.GetEnvironmentVariable("NEWSLETTER_KEEP_FIXTURES") == "1") Console.WriteLine($"Fixtures: {root}");
else Directory.Delete(root, true);
return failures == 0 ? 0 : 1;

sealed class FakeConverter(string? pdf = null) : IDocumentConverter
{
    public int Count { get; private set; }
    public Task ConvertAsync(string source, string destination, CancellationToken cancellationToken, string? originalPath = null)
    {
        Count++;
        if (pdf == null) throw new Exception("Unexpected conversion");
        File.Copy(pdf, destination);
        return Task.CompletedTask;
    }
}

sealed class RecordingCodec : IRasterCodec
{
    public List<CompressionProfile> Profiles { get; } = [];
    public EncodedRaster? Encode(RasterInput image, CompressionProfile profile) { Profiles.Add(profile); return null; }
}

sealed class CollectedProgress : IProgress<JobProgress>
{
    public List<string> Messages { get; } = [];
    public void Report(JobProgress value) => Messages.Add(value.Message);
}

sealed class RecordingSpooler : IPdfPrintSpooler
{
    public List<string> Paths { get; } = [];
    public int CapacityChecks { get; private set; }
    public int MaxConcurrent { get; private set; }
    int active;
    public int FailAt { get; init; } = -1;
    public Action<int>? CapacityAction { get; init; }
    public Task WaitForCapacityAsync(CancellationToken token)
    {
        CapacityChecks++;
        CapacityAction?.Invoke(CapacityChecks);
        token.ThrowIfCancellationRequested();
        if (CapacityChecks != Paths.Count + 1) throw new Exception("capacity not checked before submission");
        return Task.CompletedTask;
    }
    public async Task SubmitAsync(string path, CancellationToken token)
    {
        MaxConcurrent = Math.Max(MaxConcurrent, ++active);
        try
        {
            await Task.Yield();
            token.ThrowIfCancellationRequested();
            if (Paths.Count == FailAt) throw new IOException("Printer unavailable");
            Paths.Add(path);
        }
        finally { active--; }
    }
}
