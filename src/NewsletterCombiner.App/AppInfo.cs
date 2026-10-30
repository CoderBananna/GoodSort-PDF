using System.Reflection;

namespace NewsletterCombiner.App;

public static class AppInfo
{
    public static string Version => Assembly.GetExecutingAssembly().GetName().Version!.ToString(3);
    public const string RepositoryUrl = "https://github.com/CoderBananna/GoodSort-PDF";
    public static Uri RepositoryUri { get; } = new(RepositoryUrl);
    public const string Credit = "Written by Andrew";
    public const string Description = "Convert Word documents, PowerPoint presentations and images into PDFs — individually, in batches or combined.";
    public const string ModeDescription = """
        More than a newsletter combiner — a jack of all trades for preparing PDFs.

        NEWSLETTERS
        Prepare one PDF per year level or custom group, with optional shared notices. Choose Fortnightly, Weekly or a Custom issue label. Custom groups can be inferred from filenames and folders using your group list.

        BULK CONVERT
        Turn each file into its own PDF. Optionally append documents to every output, and add text or variables to the filename ending.

        COMBINE ALL
        Convert and combine files into one PDF using a single expanding file list. Arrange every document in that list and choose the output name. Notices from other modes appear at the bottom in a different colour with an Appended label, and return to the right column when you switch back.

        ARRANGE PAGES
        Drop one document or PDF to see its pages. Drag thumbnails into order, or right-click to duplicate, remove or undelete a page. Removed pages stay faded with dotted borders and are excluded when you click GO. Save a new PDF and optionally tick Add to other modes beside GO to replace its source item in the other modes, retaining its order and group. Documents opened directly are added to the shared Files/Newsletters list. Right-click a PDF in any other mode’s left list to open it here. Originals stay unchanged.

        PRINT AFTER SAVING
        Tick Print after saving beside GO in any mode. Choose a printer and settings once for the batch. All output PDFs are saved first, then sent to the printer in order, including notices and arranged pages. Existing PDFs work too: use Bulk convert to compress, save and print a folder. Printing is off when the app starts. Cancel stops further submissions; jobs already sent may still print and can be cancelled in the Windows print queue.

        All four modes include compression towards 10 MB, supported hyperlink preservation and A4 image fitting without cropping.

        Filename variables: {date} = YYYY-MM-DD, {time} = HH-mm, {n} = 001, 002, … in file order. Variables use the computer's local date/time when GO is clicked. In Combine all, {n} is 001.
        """;
    public const string ShortDescription = "Convert documents, presentations and images into PDFs — individually, in batches or combined.";
    public const string FreePromise = "Free to use. No subscriptions. No locked features. Word and PowerPoint conversion requires the corresponding desktop Microsoft Office app, supplied separately.";
    public const string OriginStory = """
        GoodSort PDF began as Newsletter Combiner, built to solve a practical school task: preparing fortnightly newsletters. Teaching teams supplied Word documents, PowerPoint presentations, images and PDFs, while notices arrived separately. Version 1 brought those files together into a consistently named PDF for each year level, with the class newsletters first and shared notices afterwards.

        Real use shaped the next steps: fitting images onto A4 without cropping, keeping hyperlinks and colours, and compressing large PDFs for distribution. It also became useful on weeks with no notices, simply converting, compressing and naming the newsletters.

        Bulk convert and Combine all grew from the same need: turn mixed files into separate PDFs, or arrange them into one document, with optional material appended and flexible filenames. The name GoodSort PDF now reflects that wider role — a jack of all trades for schools, clubs, community groups and anyone preparing documents to share.
        """;
    public const string ChangeLog = """
        1.5.1 — 30 October 2026
        • Fixed PowerPoint conversion failing with an ExportAsFixedFormat argument error.
        • Project licence included in About.

        1.5.0 — 30 October 2026
        • Optional Print after saving in all four modes, with one printer selection per batch.
        • Sequential background printing with a bounded queue; saved PDFs survive print failures.
        • Large folders are added in a single list update.

        1.4.4 — 30 October 2026
        • Arrange pages groups the checkbox and page-count summary beside GO.
        • Single-page summaries use “1 page to save”.

        1.4.3 — 30 October 2026
        • GitHub project links added to the main window and About box.
        • Documentation and screenshots refreshed.

        1.4.2 — 30 October 2026
        • Panel animation only runs when switching between one and two columns.
        • Arrange pages and Combine all keep the full-width layout steady when switching.

        1.4.1 — 30 October 2026
        • Saving arranged pages with Add to other modes replaces the source list item.
        • File order, year/custom group and appended-file placement are retained.
        • Further saves update the same entry; original files on disk stay unchanged.

        1.4.0 — 30 October 2026
        • Arrange pages mode: convert, preview, reorder and duplicate individual pages.
        • Removable pages remain faded with dotted borders and can be undeleted.
        • Optional saved-PDF handoff to the other modes and right-click editing from their left list.
        • Background thumbnail rendering, virtualized page cards and a bounded preview cache.
        • Retained pages keep their links; links to removed pages are omitted.

        1.3.1 — 30 October 2026
        • Newsletter selectors aligned to the right on the same line as the naming fields.
        • Example placeholder added to the custom Issue field.
        • Comma-separated group hint added beneath Groups.

        1.3.0 — 30 October 2026
        • Combine all expands smoothly to one file panel and restores two panels in other modes.
        • Appended files join the combined list with a distinct colour and return to their column in other modes.
        • Fortnightly, Weekly and Custom newsletter issue options.
        • Custom groups alongside the default school year levels.
        • Group names inferred from filenames and folders; ambiguous matches require a choice.
        • Newsletter preferences and separate year/custom assignments retained.

        1.2.0 — 30 October 2026
        • Renamed to GoodSort PDF to reflect its wider use.
        • Origin story and free-use promise added to About and the README.
        • Existing preferences retained from Newsletter Combiner.

        1.1.0 — 30 October 2026
        • Three modes: Newsletters, Bulk convert and Combine all.
        • Optional appended files in every mode.
        • Filename text and date, time and sequence variables.
        • Reordering and file selections retained when switching modes.
        • About expanded to explain the full PDF toolkit.

        1.0.4 — 30 October 2026
        • “Add newsletters to begin” moved beneath the Newsletters heading.
        • Duplicate prompt removed from above the author credit.

        1.0.3 — 30 October 2026
        • Description moved beneath the Newsletter Combiner title.
        • Previous year-level subtitle removed.
        • Change-log dates spaced across October.

        1.0.2 — 23 October 2026
        • Automatic PDF compression with a flexible 10 MB target.
        • Efficient image storage and progressively smaller images for oversized PDFs.
        • Text, page layout, colours and hyperlinks retained; no page flattening.
        • Clear notice if a PDF remains over 10 MB.
        • Change log added to About.

        1.0.1 — 16 October 2026
        • Prep filenames no longer include “Year”.
        • FCAC colours applied to the window and icon.
        • Author credit, description and version added to About and the footer.
        • A4 portrait/landscape image fitting verified without cropping.

        1.0.0 — 9 October 2026
        • Initial release: drag-and-drop newsletters and notices.
        • One PDF per year level, with notices appended after class newsletters.
        • Standard term/week filenames and hyperlink-preserving PDF merging.
        """;
}
