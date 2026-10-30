using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace NewsletterCombiner.Core;

public static class PdfCombiner
{
    public static void Merge(IEnumerable<string> paths, string destination, string title, CancellationToken token = default)
    {
        using var result = new PdfDocument();
        PdfSizeOptimizer.Configure(result);
        result.Info.Title = title;
        result.Info.Creator = "GoodSort PDF";
        foreach (var path in paths)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var source = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                if (source.PageCount == 0) throw new InvalidDataException("The PDF has no pages.");
                var offset = result.PageCount;
                foreach (var page in source.Pages)
                {
                    token.ThrowIfCancellationRequested();
                    result.AddPage(page);
                }
                RepairInternalLinks(source, result, offset);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                throw new InvalidDataException($"Could not combine {Path.GetFileName(path)}: {e.Message}", e);
            }
        }
        if (result.PageCount == 0) throw new InvalidDataException("There are no pages to save.");
        token.ThrowIfCancellationRequested();
        result.Save(destination);
    }

    public static void Arrange(string sourcePath, IReadOnlyList<int> pageOrder, string destination, string title, CancellationToken token = default)
    {
        if (pageOrder.Count == 0) throw new ArgumentException("Keep at least one page before saving.");
        using var source = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Import);
        using var result = new PdfDocument();
        PdfSizeOptimizer.Configure(result);
        result.Info.Title = title;
        result.Info.Creator = "GoodSort PDF";
        var imported = new Dictionary<int, PdfPage>();
        foreach (var index in pageOrder)
        {
            token.ThrowIfCancellationRequested();
            if (index < 0 || index >= source.PageCount) throw new ArgumentException("The page selection is no longer valid. Reload the document.");
            // Import resources once per source page. Duplicates share those resources, not raster previews.
            if (!imported.TryGetValue(index, out var first)) imported[index] = result.AddPage(source.Pages[index]);
            else result.AddPage((PdfPage)first.Clone());
        }
        RepairInternalLinks(source, result, pageOrder.Select((sourceIndex, targetIndex) => (sourceIndex, targetIndex)).ToArray(), true);
        token.ThrowIfCancellationRequested();
        result.Save(destination);
    }

    public static void ImageToPdf(string imagePath, string destination)
    {
        using var image = XImage.FromFile(imagePath);
        using var doc = new PdfDocument();
        PdfSizeOptimizer.Configure(doc);
        var page = doc.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        page.Orientation = image.PixelWidth > image.PixelHeight
            ? PdfSharp.PageOrientation.Landscape : PdfSharp.PageOrientation.Portrait;
        using (var graphics = XGraphics.FromPdfPage(page))
        {
            var width = page.Width.Point;
            var height = page.Height.Point;
            // Fit the complete image inside A4: one uniform scale, centred margins, no clipping or resampling.
            var scale = Math.Min(width / image.PixelWidth, height / image.PixelHeight);
            var w = image.PixelWidth * scale;
            var h = image.PixelHeight * scale;
            graphics.DrawRectangle(XBrushes.White, 0, 0, width, height);
            graphics.DrawImage(image, (width - w) / 2, (height - h) / 2, w, h);
        }
        doc.Save(destination);
    }

    static PdfItem? Resolve(PdfItem? item) => item is PdfReference r ? r.Value : item;

    // Page imports preserve URI/mailto actions and colour resources. Local destinations must point to
    // the newly inserted pages, including named destinations used by Word's table-of-contents links.
    static void RepairInternalLinks(PdfDocument source, PdfDocument target, int offset) =>
        RepairInternalLinks(source, target, Enumerable.Range(0, source.PageCount).Select(i => (i, offset + i)).ToArray(), false);

    static void RepairInternalLinks(PdfDocument source, PdfDocument target, (int Source, int Target)[] order, bool allowRemoved)
    {
        var pageIndexes = Enumerable.Range(0, source.PageCount).ToDictionary(i => source.Pages[i].Reference!, i => i);
        var named = new Dictionary<string, PdfItem>(StringComparer.Ordinal);
        var catalog = source.Internals.Catalog;
        if (Resolve(catalog.Elements["/Dests"]) is PdfDictionary legacy)
            foreach (var key in legacy.Elements.Keys) named[key.TrimStart('/')] = legacy.Elements[key]!;
        if (Resolve(catalog.Elements["/Names"]) is PdfDictionary names && Resolve(names.Elements["/Dests"]) is PdfDictionary root)
            ReadNames(root, named, new HashSet<PdfDictionary>());

        var firstTargets = order.GroupBy(p => p.Source).ToDictionary(g => g.Key, g => g.First().Target);

        PdfArray? Map(PdfItem? value, int currentSource, int currentTarget)
        {
            value = Resolve(value);
            var key = value switch { PdfString s => s.Value, PdfName n => n.Value.TrimStart('/'), _ => null };
            if (key != null) value = named.TryGetValue(key, out var found) ? Resolve(found) : null;
            if (value is PdfDictionary d) value = Resolve(d.Elements["/D"]);
            if (value is not PdfArray array || array.Elements.Count < 2)
                throw new InvalidDataException("A local hyperlink has an unreadable destination.");
            var page = Resolve(array.Elements[0]) as PdfDictionary;
            if (page?.Reference == null || !pageIndexes.TryGetValue(page.Reference, out var index))
                throw new InvalidDataException("A local hyperlink points to a missing page.");
            if (!firstTargets.TryGetValue(index, out var destinationIndex))
            {
                if (allowRemoved) return null;
                throw new InvalidDataException("A local hyperlink points to a missing page.");
            }
            if (index == currentSource) destinationIndex = currentTarget;
            var mapped = new PdfArray(target);
            mapped.Elements.Add(target.Pages[destinationIndex].Reference!);
            for (var j = 1; j < array.Elements.Count; j++)
                mapped.Elements.Add((Resolve(array.Elements[j]) ?? PdfNull.Value).Clone());
            return mapped;
        }

        foreach (var (sourceIndex, targetIndex) in order)
        {
            var oldAnnotations = source.Pages[sourceIndex].Elements.GetArray("/Annots");
            var importedAnnotations = target.Pages[targetIndex].Elements.GetArray("/Annots");
            if (oldAnnotations == null || importedAnnotations == null) continue;
            var annotations = new PdfArray(target);
            for (var j = 0; j < oldAnnotations.Elements.Count; j++)
            {
                var oldLink = oldAnnotations.Elements.GetDictionary(j);
                var importedLink = importedAnnotations.Elements.GetDictionary(j);
                if (oldLink == null || importedLink == null) continue;
                var newLink = new PdfDictionary(target);
                foreach (var key in importedLink.Elements.Keys) newLink.Elements[key] = importedLink.Elements[key]!.Clone();
                newLink.Elements["/P"] = target.Pages[targetIndex].Reference!;
                if (oldLink.Elements.GetName("/Subtype") == "/Link")
                {
                    if (oldLink.Elements.ContainsKey("/Dest"))
                    {
                        var mapped = Map(oldLink.Elements["/Dest"], sourceIndex, targetIndex);
                        if (mapped == null) continue;
                        newLink.Elements["/Dest"] = mapped;
                    }
                    if (Resolve(oldLink.Elements["/A"]) is PdfDictionary oldAction && oldAction.Elements.GetName("/S") == "/GoTo")
                    {
                        var mapped = Map(oldAction.Elements["/D"], sourceIndex, targetIndex);
                        if (mapped == null) continue;
                        var action = new PdfDictionary(target);
                        action.Elements.SetName("/S", "/GoTo");
                        action.Elements["/D"] = mapped;
                        newLink.Elements["/A"] = action;
                    }
                }
                target.Internals.AddObject(newLink);
                annotations.Elements.Add(newLink.Reference!);
            }
            target.Pages[targetIndex].Elements["/Annots"] = annotations;
        }
    }

    static void ReadNames(PdfDictionary node, Dictionary<string, PdfItem> names, HashSet<PdfDictionary> visited)
    {
        if (!visited.Add(node)) return;
        if (Resolve(node.Elements["/Names"]) is PdfArray pairs)
            for (var i = 0; i + 1 < pairs.Elements.Count; i += 2)
                if (Resolve(pairs.Elements[i]) is PdfString name) names[name.Value] = pairs.Elements[i + 1];
        if (Resolve(node.Elements["/Kids"]) is PdfArray kids)
            foreach (var kid in kids.Elements)
                if (Resolve(kid) is PdfDictionary child) ReadNames(child, names, visited);
    }
}
