# Development guide

Build instructions and verification notes for GoodSort PDF maintainers. Run the commands below from the repository root. For app workflows, see the [user guide](USER-GUIDE.md); for the project overview, see the [README](../README.md).

## Build from source

Install the **.NET 10 SDK**, clone this repository, and run:

```powershell
./build.ps1
```

The script runs the core tests and publishes the self-contained Windows executable to `dist/GoodSort PDF.exe`.

Equivalent commands, including for cross-compiling on Linux:

```sh
dotnet run --project tests/NewsletterCombiner.Tests -c Release
dotnet publish src/NewsletterCombiner.App/NewsletterCombiner.App.csproj -c Release -r win-x64 --self-contained true -o dist
```

The application uses WPF and .NET 10, with Windows’ native PDF renderer for page thumbnails. PDF composition is handled by PDFsharp 6.2.4. Office export runs in a separate STA worker inside the same executable, keeping the interface responsive.

### Project layout

| Folder | Purpose |
| --- | --- |
| `src/NewsletterCombiner.App` | Windows interface, image conversion and Microsoft Office export. |
| `src/NewsletterCombiner.Core` | Year grouping, filenames, PDF merging, compression and batch output. |
| `tests/NewsletterCombiner.Tests` | Automated core checks. |
| `tests/fixtures` | Generated image fixtures for layout checks. |
| `tests/WindowsSmoke.ps1` | Windows Office export checks and fixtures for a manual interface check. |
| `tests/WindowsModes.ps1` | Windows mode switching, image conversion, ordering, appending and filename checks using generated documents. |
| `tests/WindowsPrint.ps1` | Save-and-print checks using Microsoft Print to PDF with explicit temporary output files; no physical printing. Includes a 10,000-file folder-import check. |
| `tests/WindowsPages.ps1` | Windows page-editor checks, Word/image conversion, optional handoff, scrolling and bounded thumbnail caching. Requires desktop Word. |
| `tools/PdfInspector` | PDF structure inspection for validation. |

### Verification

Version **1.5.0** passes **50 core checks**, **51 Windows mode checks**, **37 Windows page-editor checks** and **34 Windows printing checks**. The printing checks exercise the actual Windows driver using temporary Microsoft Print to PDF outputs. Importing a generated folder containing 10,000 PDFs took about **0.46 seconds** on USS-DISCOVERY; the core runner also completed 10,000 simulated submissions in order. This is not a benchmark of 10,000 Office conversions or physical print jobs.

The core suite now includes the original **46 checks** plus four batch-printing checks for ordered submission, a 10,000-output simulated batch, cancellation and failure without changing saved PDFs. These include page selection, duplication, internal-link remapping, removed destinations, source integrity and safe publishing. They cover newsletter grouping, ordering, filenames, page content, web and email links, internal links, A4 image layout, source integrity, cancellation, conversion failures, existing-output protection and compression. Additional cases cover bulk outputs, optional appended documents, combined output order, filename variables, name collisions, weekly/custom newsletter names and deterministic custom-group inference.

Version 1.5.0 passes **51 native mode checks** covering mode changes with loaded files, retained year assignments, reordering through the arrow control, image-to-PDF conversion with and without appended files, A4 orientation, appended hyperlinks, combined page order, filename validation, custom groups/issues, appended-item transfers, animated transitions and window resizing. They also check that same-column-count switches do not animate, including Arrange pages ↔ Combine all and rapid switches during an earlier expansion. Run `tests/WindowsModes.ps1` on Windows to reproduce these checks; it generates temporary documents and interface previews without loading or saving your app preferences.

Version 1.5.0 passes **37 page-editor Windows checks**, covering replacement in place, retained order and groups, repeated saves, failed-save recovery, standalone additions, appended-item identity, thumbnails, reorder/duplicate/remove/undelete, actual context-menu actions, saving and optional handoff, invalid-document recovery, Word/image conversion, temporary-file cleanup and virtualized scrolling through a generated 400-page PDF. Run `tests/WindowsPages.ps1` with desktop Word installed. In the final desktop-session check on USS-DISCOVERY, a generated 400-page PDF opened in about 0.24 seconds and its first visible thumbnails were ready within 0.40 seconds; 8 previews were rendered initially and the cache stayed within 48 entries. Real-document timings depend on content and hardware.

PowerPoint automation was not independently re-tested for 1.5.0; that exporter is unchanged. Run `tests/WindowsSmoke.ps1` on a Windows PC with Word and PowerPoint for the Office check and manual end-to-end instructions. The earlier Windows compression check reduced a 38.1 MB newsletter to 8.5 MB.

In a Windows validation run, a three-page newsletter reduced from **38.1 MB to 8.5 MB**, with its page drawing commands unchanged. Results depend on the source files.

Detailed Windows check results are retained in [verification/](verification/).

## Printing implementation

The printer dialog is shown once before conversion starts. Its validated PrintTicket is converted to DEVMODE so the selected paper, orientation, colour, copies and duplex settings reach the installed driver. Printing is opt-in on each app launch and is not persisted.

The Windows PDF renderer prepares one page at a time, at up to 300 dpi with a 12-million-pixel cap. The GDI print path fits and centres each page within the printer's imageable area, using the device's horizontal and vertical resolutions. Only the print rendition is rasterized; the saved PDF keeps its original text, vectors and supported links. An external PDF reader is not needed. Driver colour handling can affect physical output.

A dedicated STA worker submits one job per saved PDF. At most two jobs from the batch are outstanding before it waits for queue capacity; it does not wait for unrelated users' jobs. Cancellation aborts the current submission and stops future submissions. Jobs already submitted remain under Windows queue control, so completion is reported as “sent to printer”, not as physical print completion. Queue errors stop the batch without automatic retries or deleting saved files.

`PrintBatch` is independent of the Windows backend, which lets the core tests exercise ordering, 10,000 simulated submissions and cancellation without a printer. `WindowsPrint.ps1` uses the actual Windows driver and compares saved/printed page counts for all four modes; its print-to-file override is only supplied by the internal check runner. Physical printer behaviour and a full 10,000-document Office conversion/print run have not been tested.

API references: [Windows PDF page rendering](https://learn.microsoft.com/en-us/uwp/api/windows.data.pdf.pdfpage.rendertostreamasync), [PrintTicket to DEVMODE](https://learn.microsoft.com/en-us/dotnet/api/system.printing.interop.printticketconverter.convertprinttickettodevmode), and [GDI print functions](https://learn.microsoft.com/en-us/windows/win32/printdocs/gdi-print-api-functions).

## Group inference

The app matches configured group names against the filename first, then up to three containing folders. Matching ignores letter case and treats spaces, underscores and hyphens as separators. It matches whole names, prefers `Sea Scouts` over the less specific `Scouts`, and leaves conflicting matches blank for you to choose. Editing the group list refreshes automatic suggestions without replacing your manual choices. You can always select or type a group yourself. School year and custom group assignments are retained separately when you toggle between them.

## Page previews and link handling

Page previews use Windows’ built-in PDF renderer in the background. The app creates cards only around the visible area and keeps at most 48 small thumbnails in its cache. Saving uses the PDF page content, not the preview images, so text and vector drawings remain intact. Web/email links are preserved; internal links follow retained pages in their new positions. Links whose destination page was removed are omitted. When a destination has duplicates, links use the first retained copy; a page’s link to itself stays with that copy.

## Image preparation and compression

Camera orientation is corrected, tagged images are converted to sRGB, and transparent areas receive a white background.

For large input images, the app limits the long edge to 2,480 pixels and chooses the smaller of PNG and high-quality JPEG storage. For oversized PDFs, image compression considers the largest placement of each image on the page and uses four bounded quality settings. Unsupported colour spaces, transparency masks and custom image decoding are left untouched to avoid changing their appearance. Existing RGB ICC profiles are retained.

## Stored preferences

Only mode, naming, newsletter frequency, custom groups, issue label, term, week, output-folder and add-to-other-modes preferences are stored in `%LOCALAPPDATA%\Newsletter Combiner\settings.json`. The original folder name is retained so upgrading to GoodSort PDF keeps your existing preferences.

The Print after saving checkbox is not persisted and starts unchecked on each app launch.
