using System.IO;
using System.Runtime.InteropServices;

namespace NewsletterCombiner.App;

internal static class OfficeExporter
{
    public static void Export(string source, string destination, string originalPath)
    {
        var presentation = Path.GetExtension(source).ToLowerInvariant() is ".ppt" or ".pptx";
        var program = presentation ? "PowerPoint.Application" : "Word.Application";
        var type = Type.GetTypeFromProgID(program) ?? throw new InvalidOperationException($"Install desktop {(presentation ? "PowerPoint" : "Word")} to use this file type.");
        dynamic? app = null;
        dynamic? documents = null;
        dynamic? document = null;
        dynamic? options = null;
        int? oldSecurity = null;
        int? oldAlerts = null;
        bool? oldLinks = null;
        bool? oldBackground = null;
        try
        {
            app = Activator.CreateInstance(type)!;
            oldSecurity = (int)app.AutomationSecurity;
            oldAlerts = (int)app.DisplayAlerts;
            app.AutomationSecurity = 3; // msoAutomationSecurityForceDisable: never execute submitted macros.
            if (presentation)
            {
                app.DisplayAlerts = 1; // ppAlertsNone
                documents = app.Presentations;
                document = documents.Open(Path.GetFullPath(source), 0, 0, 0);
                ResolveRelativeLinks(document, originalPath, presentation: true);
                ExportPresentationPdf(document, destination);
            }
            else
            {
                app.DisplayAlerts = 0;
                options = app.Options;
                oldLinks = (bool)options.UpdateLinksAtOpen;
                oldBackground = (bool)options.PrintBackgrounds;
                options.UpdateLinksAtOpen = false;
                options.PrintBackgrounds = true;
                documents = app.Documents;
                document = documents.Open(FileName: Path.GetFullPath(source), ConfirmConversions: false,
                    ReadOnly: false, AddToRecentFiles: false, Visible: false, NoEncodingDialog: true,
                    PasswordDocument: "", PasswordTemplate: "");
                ResolveRelativeLinks(document, originalPath, presentation: false);
                document.ExportAsFixedFormat(OutputFileName: Path.GetFullPath(destination), ExportFormat: 17,
                    OpenAfterExport: false, OptimizeFor: 0, Range: 0, Item: 0, IncludeDocProps: true,
                    KeepIRM: true, CreateBookmarks: 1, DocStructureTags: true, BitmapMissingFonts: true,
                    UseISO19005_1: false);
            }
        }
        catch (COMException e)
        {
            throw new IOException($"{(presentation ? "PowerPoint" : "Word")} could not export this document. Open it in Office to check for a password, protection, or repair prompt. {e.Message}", e);
        }
        finally
        {
            if (document != null)
            {
                try { if (presentation) { document.Saved = -1; document.Close(); } else document.Close(0); } catch { }
                Release(document);
            }
            if (options != null)
            {
                try { if (oldLinks.HasValue) options.UpdateLinksAtOpen = oldLinks.Value; if (oldBackground.HasValue) options.PrintBackgrounds = oldBackground.Value; } catch { }
                Release(options);
            }
            if (app != null)
            {
                try
                {
                    if (oldSecurity.HasValue) app.AutomationSecurity = oldSecurity.Value;
                    if (oldAlerts.HasValue) app.DisplayAlerts = oldAlerts.Value;
                    // PowerPoint may reuse an existing application; leave any other open documents alone.
                    if (documents != null && (int)documents!.Count == 0) app!.Quit();
                }
                catch { }
            }
            Release(documents);
            Release(app);
        }
    }

    static void ExportPresentationPdf(dynamic document, string destination)
    {
        dynamic? slides = null;
        dynamic? printOptions = null;
        dynamic? ranges = null;
        dynamic? range = null;
        try
        {
            slides = document.Slides;
            var count = (int)slides.Count;
            if (count == 0) throw new InvalidDataException("This presentation has no slides to export.");
            printOptions = document.PrintOptions;
            ranges = printOptions.Ranges;
            // PowerPoint requires a PrintRange COM object. A late-bound C# null is
            // marshalled as the wrong variant type and fails with "argument 7".
            range = ranges.Add(1, count);
            document.ExportAsFixedFormat(Path.GetFullPath(destination), 2, 2, 0, 1, 1, 0,
                range, 1, "", true, true, true, true, false);
        }
        finally
        {
            Release(range);
            Release(ranges);
            Release(printOptions);
            Release(slides);
        }
    }

    static void Release(object? value)
    {
        if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }

    static void ResolveRelativeLinks(dynamic document, string originalPath, bool presentation)
    {
        var baseUri = new Uri(Path.GetDirectoryName(Path.GetFullPath(originalPath))! + Path.DirectorySeparatorChar);
        dynamic? properties = null;
        dynamic? property = null;
        try
        {
            properties = document.BuiltInDocumentProperties;
            property = properties["Hyperlink Base"];
            var value = Convert.ToString(property.Value);
            if (!string.IsNullOrWhiteSpace(value)) baseUri = new Uri(baseUri, value.TrimEnd('/', '\\') + "/");
        }
        catch (COMException) { } // The property can be unset in a new document.
        finally { Release(property); Release(properties); }

        if (!presentation)
        {
            dynamic hyperlinks = document.Hyperlinks;
            try { Absolutize(hyperlinks, baseUri); } finally { Release(hyperlinks); }
            return;
        }
        dynamic slides = document.Slides;
        try
        {
            for (var i = 1; i <= (int)slides.Count; i++)
            {
                dynamic slide = slides[i];
                dynamic hyperlinks = slide.Hyperlinks;
                try { Absolutize(hyperlinks, baseUri); } finally { Release(hyperlinks); Release(slide); }
            }
        }
        finally { Release(slides); }
    }

    static void Absolutize(dynamic hyperlinks, Uri baseUri)
    {
        for (var i = 1; i <= (int)hyperlinks.Count; i++)
        {
            dynamic hyperlink = hyperlinks[i];
            try
            {
                string address = Convert.ToString(hyperlink.Address) ?? "";
                if (address.Length > 0 && !address.StartsWith('#') && !Uri.TryCreate(address, UriKind.Absolute, out _))
                    hyperlink.Address = new Uri(baseUri, address.Replace('\\', '/')).AbsoluteUri;
            }
            finally { Release(hyperlink); }
        }
    }
}
