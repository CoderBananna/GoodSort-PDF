# Privacy and local data

GoodSort PDF converts and processes documents on your Windows PC. It does not upload documents to a conversion service, include advertising or analytics, or send automatic usage or crash reports. Conversion does not need an internet connection.

## Connections you choose

- Selecting **Print after saving** sends the finished PDFs through Windows to the printer you select. Network or cloud printers may transfer documents according to their own settings and privacy policies.
- Choosing a network drive or cloud-synchronised input/output folder lets that service handle the files. Choose local folders if you want them to remain exclusively on this PC.
- Clicking the GitHub credit link opens the project in your browser, subject to [GitHub's privacy statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement).
- Word and PowerPoint conversions use your separately installed Microsoft Office applications. GoodSort PDF disables Office macros during export and disables Word link updates during opening; it does not control Office licensing, connected experiences, Windows services or printer-driver network behaviour. Their settings and the [Microsoft privacy statement](https://privacy.microsoft.com/privacystatement) apply separately.

GoodSort PDF itself has no automatic document-upload, telemetry or update-checking service. PDFsharp performs PDF processing locally. Supported hyperlinks are retained in output PDFs; following those links later is a separate action in your PDF reader.

## Files stored on this PC

Preferences such as the selected mode, naming settings, groups and output folder are saved in `%LOCALAPPDATA%\Newsletter Combiner\settings.json`. The older folder name keeps existing preferences working after upgrades. Printing is off at launch and is not stored as a preference.

Processing makes temporary document copies in `.newsletter-*` and `.goodsort-pages-*` folders inside the selected output folder. Page previews use `%TEMP%\GoodSort-pages-*`. These folders are normally removed when work finishes or the preview closes; a crash or interrupted cleanup can leave files behind. The bundled .NET runtime can also extract support files under `%TEMP%\.net`. Windows and printer drivers manage their own print-spool files.

Your source documents remain unchanged. Saved PDFs remain in your chosen output folder.

## Remove the app and its data

1. Close GoodSort PDF and delete its executable and any shortcuts you created. There is no installer, service or startup entry to remove.
2. Optionally delete `%LOCALAPPDATA%\Newsletter Combiner` to remove saved preferences.
3. With the app closed, you may remove leftover temporary folders with the exact prefixes above. Inside `%TEMP%\.net`, remove only the GoodSort PDF extraction folder if present; other apps use this location too.
4. Keep or delete your output PDFs yourself. If printing was interrupted, manage outstanding jobs through the Windows print queue; deleting the app does not cancel those jobs.

Removing GoodSort PDF does not uninstall Microsoft Office, Windows components or printer drivers.
