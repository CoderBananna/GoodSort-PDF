using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Navigation;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using NewsletterCombiner.Core;

namespace NewsletterCombiner.App;

public partial class MainWindow : Window
{
    readonly bool persistPreferences;
    bool ready;
    bool changingMode;
    ConversionMode previousMode;
    FileEntry? editorOrigin;
    bool closeApproved;
    internal ConversionMode Mode => ArrangeMode.IsChecked == true ? ConversionMode.Arrange : BulkMode.IsChecked == true ? ConversionMode.Bulk : CombineMode.IsChecked == true ? ConversionMode.Combine : ConversionMode.Newsletters;
    internal NewsletterOptions NewsletterSettings => new(
        WeeklyChoice.IsChecked == true ? NewsletterFrequency.Weekly : CustomIssueChoice.IsChecked == true ? NewsletterFrequency.Custom : NewsletterFrequency.Fortnightly,
        CustomGroupsChoice.IsChecked == true, IssueBox.Text);
    string groupError = "";
    public static readonly DependencyProperty NoticeFractionProperty = DependencyProperty.Register(nameof(NoticeFraction), typeof(double), typeof(MainWindow),
        new PropertyMetadata(1d, (d, e) => ((MainWindow)d).LayoutPanels((double)e.NewValue)));
    public double NoticeFraction { get => (double)GetValue(NoticeFractionProperty); set => SetValue(NoticeFractionProperty, value); }
    void LayoutPanels(double fraction)
    {
        if (NoticeColumn == null) return;
        NoticeColumn.Width = new GridLength(fraction, GridUnitType.Star);
        FileGap.Width = new GridLength(18 * fraction);
        NoticeClip.Opacity = fraction;
        NoticesPanel.Visibility = fraction < 0.001 ? Visibility.Collapsed : Visibility.Visible;
    }
    void AnimatePanels()
    {
        static bool SingleColumn(ConversionMode mode) => mode is ConversionMode.Combine or ConversionMode.Arrange;
        var target = SingleColumn(Mode) ? 0d : 1d;
        var layoutChanged = SingleColumn(previousMode) != SingleColumn(Mode);
        NoticeClip.IsHitTestVisible = target > 0;
        // Animate only when the column count changes. Same-layout switches settle any earlier transition.
        // Reversing a layout transition starts at its current width.
        var current = NoticeFraction;
        BeginAnimation(NoticeFractionProperty, null);
        NoticeFraction = target;
        if (layoutChanged && IsLoaded && SystemParameters.ClientAreaAnimation && Math.Abs(current - target) > 0.001)
            BeginAnimation(NoticeFractionProperty, new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(280))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }, FillBehavior = FillBehavior.Stop });
        else LayoutPanels(target);
    }
    CancellationTokenSource? cancellation;
    string? lastOutput;
    internal Func<PrinterSelection?> SelectPrinter { get; set; } = PrinterSelection.Choose;
    internal Func<IReadOnlyList<string>, PrinterSelection, IProgress<PrintProgress>?, CancellationToken, Task<PrintResult>> PrintSavedOutputs { get; set; }
        = (paths, printer, progress, token) => PdfPrinter.PrintAsync(paths, printer, progress, token);
    // Keep the original preferences location so existing installations retain their settings.
    readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Newsletter Combiner", "settings.json");
    record Preferences(int Term, int FirstWeek, int LastWeek, string Folder, ConversionMode Mode = ConversionMode.Newsletters, string Ending = "", string CombinedName = "Combined document", NewsletterFrequency Frequency = NewsletterFrequency.Fortnightly, bool CustomGroups = false, string Groups = "Joeys, Cubs, Scouts", string Issue = "", bool AddArranged = false);

    public MainWindow() : this(true) { }

    internal MainWindow(bool persistPreferences)
    {
        this.persistPreferences = persistPreferences;
        InitializeComponent();
        TermBox.ItemsSource = Enumerable.Range(1, 4);
        FirstWeekBox.ItemsSource = Enumerable.Range(1, 12);
        LastWeekBox.ItemsSource = Enumerable.Range(1, 12);
        Preferences? settings = null;
        try { if (persistPreferences && File.Exists(settingsPath)) settings = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(settingsPath)); } catch { }
        TermBox.SelectedItem = Math.Clamp(settings?.Term ?? 1, 1, 4);
        FirstWeekBox.SelectedItem = Math.Clamp(settings?.FirstWeek ?? 1, 1, 12);
        LastWeekBox.SelectedItem = Math.Clamp(settings?.LastWeek ?? 2, (int)FirstWeekBox.SelectedItem, 12);
        OutputFolder.Text = settings?.Folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GoodSort PDFs");
        NewslettersPanel.Changed += (_, _) => Refresh();
        NoticesPanel.Changed += (_, _) => Refresh();
        PageEditorControl.Changed += (_, _) => Refresh();
        PageEditorControl.OpenRequested += async path => await OpenEditorAsync(path);
        NewslettersPanel.CanArrangePages = true;
        NewslettersPanel.ArrangeRequested += async path => await OpenEditorAsync(path);
        AddArrangedCheckBox.IsChecked = settings?.AddArranged ?? false;
        SuffixBox.Text = settings?.Ending ?? "";
        CombinedNameBox.Text = settings?.CombinedName ?? "Combined document";
        GroupNamesBox.Text = settings?.Groups ?? "Joeys, Cubs, Scouts";
        IssueBox.Text = settings?.Issue ?? "";
        if (settings?.Frequency == NewsletterFrequency.Weekly) WeeklyChoice.IsChecked = true;
        else if (settings?.Frequency == NewsletterFrequency.Custom) CustomIssueChoice.IsChecked = true;
        if (settings?.CustomGroups == true) CustomGroupsChoice.IsChecked = true;
        ready = true;
        if (settings?.Mode == ConversionMode.Bulk) BulkMode.IsChecked = true;
        else if (settings?.Mode == ConversionMode.Combine) CombineMode.IsChecked = true;
        else if (settings?.Mode == ConversionMode.Arrange) ArrangeMode.IsChecked = true;
        ApplyMode();
    }

    void Refresh()
    {
        if (!ready || changingMode || cancellation != null) return;
        if (Mode == ConversionMode.Arrange)
        {
            var validName = true;
            try { DocumentPlan.CombinedFileName(ArrangedNameBox.Text, DateTime.Now); }
            catch (ArgumentException) { validName = false; }
            GoButton.IsEnabled = PageEditorControl.PdfPath != null && PageEditorControl.KeptCount > 0 && validName;
            StatusText.Text = PageEditorControl.PdfPath == null ? "" : PageEditorControl.KeptCount == 0 ? "Keep at least one page to save" : !validName ? "Enter a valid PDF name" : $"{PageEditorControl.KeptCount} page{(PageEditorControl.KeptCount == 1 ? "" : "s")} to save";
            return;
        }
        var count = NewslettersPanel.Files.Count;
        var error = "";
        if (Mode == ConversionMode.Newsletters)
        {
            var groups = NewslettersPanel.Files.Select(f => f.Assignment).Where(y => !string.IsNullOrWhiteSpace(y)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var missing = NewslettersPanel.Files.Count(f => string.IsNullOrWhiteSpace(f.Assignment));
            try
            {
                if (NewsletterSettings.CustomGroups && groupError.Length > 0) throw new ArgumentException(groupError);
                groups = groups.Select(g => NewsletterSettings.CustomGroups ? NewsletterPlan.NormalizeGroup(g) : NewsletterPlan.NormalizeYear(g))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                NamingPreview.Text = NewsletterPlan.OutputFileName(groups.FirstOrDefault() ?? (NewsletterSettings.CustomGroups ? "Group" : "X"),
                    (int)TermBox.SelectedItem, (int)FirstWeekBox.SelectedItem, (int)LastWeekBox.SelectedItem, NewsletterSettings);
            }
            catch (ArgumentException ex) { error = ex.Message; NamingPreview.Text = "Check newsletter naming"; }
            StatusText.Text = error.Length > 0 ? error : count == 0 ? "" : missing > 0 ? $"Choose a {(NewsletterSettings.CustomGroups ? "group" : "year level")} for {missing} newsletter{(missing == 1 ? "" : "s")}" : $"{groups.Length} PDF{(groups.Length == 1 ? "" : "s")} · newsletters first, then notices";
            GoButton.IsEnabled = count > 0 && missing == 0 && error.Length == 0;
        }
        else
        {
            try
            {
                DocumentPreview.Text = Mode == ConversionMode.Bulk
                    ? DocumentPlan.BulkFileName(NewslettersPanel.Files.FirstOrDefault()?.Path ?? "Document.docx", SuffixBox.Text, DateTime.Now, 1)
                    : DocumentPlan.CombinedFileName(CombinedNameBox.Text, DateTime.Now);
            }
            catch (ArgumentException ex) { error = ex.Message; DocumentPreview.Text = "Check filename"; }
            StatusText.Text = error.Length > 0 ? error : count == 0 ? "" : Mode == ConversionMode.Bulk
                ? $"{count} PDF{(count == 1 ? "" : "s")} · one per file"
                : "1 PDF · files in displayed order";
            GoButton.IsEnabled = count > 0 && error.Length == 0;
        }
    }

    void Mode_Changed(object sender, RoutedEventArgs e) { if (ready) ApplyMode(); }
    void ApplyMode()
    {
        changingMode = true;
        if (Mode == ConversionMode.Combine && previousMode != ConversionMode.Combine)
        {
            foreach (var file in NoticesPanel.Files.ToArray())
            {
                NoticesPanel.Files.Remove(file);
                file.IsAppended = true;
                NewslettersPanel.Files.Add(file);
            }
        }
        else if (Mode != ConversionMode.Combine && previousMode == ConversionMode.Combine)
        {
            foreach (var file in NewslettersPanel.Files.Where(f => f.IsAppended).ToArray())
            {
                NewslettersPanel.Files.Remove(file);
                file.IsAppended = false;
                NoticesPanel.Files.Add(file);
            }
        }
        var newsletter = Mode == ConversionMode.Newsletters;
        NamingControls.Visibility = newsletter ? Visibility.Visible : Visibility.Collapsed;
        var arrange = Mode == ConversionMode.Arrange;
        DocumentNaming.Visibility = !newsletter && !arrange ? Visibility.Visible : Visibility.Collapsed;
        ArrangeNaming.Visibility = PageEditorControl.Visibility = ArrangeFooter.Visibility = AddArrangedCheckBox.Visibility = arrange ? Visibility.Visible : Visibility.Collapsed;
        FileColumns.Visibility = StatusText.Visibility = arrange ? Visibility.Collapsed : Visibility.Visible;
        SuffixBox.Visibility = Mode == ConversionMode.Bulk ? Visibility.Visible : Visibility.Collapsed;
        CombinedNameBox.Visibility = Mode == ConversionMode.Combine ? Visibility.Visible : Visibility.Collapsed;
        DocumentNameLabel.Text = Mode == ConversionMode.Bulk ? "Filename ending" : "PDF name";
        NewslettersPanel.IsNewsletter = newsletter;
        NewslettersPanel.Heading = newsletter ? "Newsletters" : "Files";
        NewslettersPanel.Subheading = newsletter ? "Add newsletters to begin" : Mode == ConversionMode.Bulk ? "One PDF per file" : "Combined in this order";
        NoticesPanel.Heading = newsletter ? "Notices" : "Append at end";
        NoticesPanel.Subheading = newsletter ? "Added after every year’s newsletters" : Mode == ConversionMode.Bulk ? "Optional · added to each PDF" : "Optional · added after all files";
        ApplyNewsletterOptions();
        AnimatePanels();
        previousMode = Mode;
        OpenFolderButton.Visibility = Visibility.Collapsed;
        changingMode = false;
        Refresh();
    }
    void NewsletterOptions_Changed(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        changingMode = true;
        ApplyNewsletterOptions();
        changingMode = false;
        Refresh();
    }
    void GroupNames_Changed(object sender, TextChangedEventArgs e) => NewsletterOptions_Changed(sender, e);
    void ApplyNewsletterOptions()
    {
        var options = NewsletterSettings;
        CustomGroupsEditor.Visibility = options.CustomGroups ? Visibility.Visible : Visibility.Collapsed;
        TermWeekControls.Visibility = options.Frequency == NewsletterFrequency.Custom ? Visibility.Collapsed : Visibility.Visible;
        CustomIssueControls.Visibility = options.Frequency == NewsletterFrequency.Custom ? Visibility.Visible : Visibility.Collapsed;
        LastWeekControls.Visibility = options.Frequency == NewsletterFrequency.Weekly ? Visibility.Collapsed : Visibility.Visible;
        WeeksLabel.Text = options.Frequency == NewsletterFrequency.Weekly ? "Week" : "Weeks";
        string[] names = [];
        groupError = "";
        try { names = NewsletterPlan.ParseGroups(GroupNamesBox.Text); }
        catch (ArgumentException ex) { groupError = ex.Message; }
        NewslettersPanel.ConfigureGrouping(options.CustomGroups, names);
    }
    void DocumentName_Changed(object sender, TextChangedEventArgs e) => Refresh();
    void Variable_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
    }
    void InsertVariable_Click(object sender, RoutedEventArgs e)
    {
        var box = Mode == ConversionMode.Bulk ? SuffixBox : CombinedNameBox;
        var variable = (string)((MenuItem)sender).Tag;
        var position = box.SelectionStart;
        box.SelectedText = variable;
        box.CaretIndex = position + variable.Length;
        box.Focus();
    }
    internal IReadOnlyList<OutputPlan> BuildPlans(DateTime timestamp) => Mode switch
    {
        ConversionMode.Bulk => DocumentPlan.CreateBulk(NewslettersPanel.Files.Select(f => f.Path), NoticesPanel.Files.Select(f => f.Path), SuffixBox.Text, timestamp),
        ConversionMode.Combine => DocumentPlan.CreateCombined(NewslettersPanel.Files.Select(f => f.Path), [], CombinedNameBox.Text, timestamp),
        _ => NewsletterPlan.Create(NewslettersPanel.Files.Select(f => new Newsletter(f.Path, f.Assignment)), NoticesPanel.Files.Select(f => f.Path),
            (int)TermBox.SelectedItem, (int)FirstWeekBox.SelectedItem, (int)LastWeekBox.SelectedItem, NewsletterSettings)
    };

    void Naming_Changed(object sender, SelectionChangedEventArgs e) => Refresh();
    void FirstWeek_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        if (FirstWeekBox.SelectedItem is int first) LastWeekBox.SelectedItem = Math.Min(first + 1, 12);
        Refresh();
    }
    void Folder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Save PDFs", InitialDirectory = Directory.Exists(OutputFolder.Text) ? OutputFolder.Text : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) };
        if (dialog.ShowDialog(this) == true) { OutputFolder.Text = dialog.FolderName; SaveSettings(); }
    }

    async void Go_Click(object sender, RoutedEventArgs e)
    {
        if (Mode == ConversionMode.Arrange) await SaveArrangementAsync();
        else await RunBatchAsync();
    }

    internal async Task RunBatchAsync(bool prompt = true)
    {
        if (cancellation != null) return;
        try
        {
            var plans = BuildPlans(DateTime.Now);
            if (string.IsNullOrWhiteSpace(OutputFolder.Text)) throw new ArgumentException("Choose an output folder.");
            var folder = OutputFolder.Text;
            var printer = PrintAfterSavingCheckBox.IsChecked == true ? SelectPrinter() : null;
            if (PrintAfterSavingCheckBox.IsChecked == true && printer == null) return;
            SaveSettings();
            cancellation = new CancellationTokenSource();
            SetBusy(true);
            var progress = new Progress<JobProgress>(p => { StatusText.Text = p.Message; Progress.Maximum = p.Total; Progress.Value = p.Completed; });
            var token = cancellation.Token;
            var outputs = await Task.Run(() => new CombineJob(new DocumentConverter(), new PdfSizeOptimizer(new WpfRasterCodec())).RunAsync(plans, folder, progress, token));
            lastOutput = folder;
            OpenFolderButton.Visibility = Visibility.Visible;
            StatusText.Text = $"Saved {outputs.Count} PDF{(outputs.Count == 1 ? "" : "s")}";
            if (printer != null) await PrintSavedAsync(outputs, printer, StatusText.Text, prompt);
            var oversized = outputs.Select(p => new FileInfo(p)).Where(f => f.Length > PdfSizeOptimizer.TargetBytes).ToArray();
            if (oversized.Length > 0)
            {
                StatusText.Text += $" · {oversized.Length} over 10 MB";
                if (prompt) MessageBox.Show(this, "Saved with the smallest version available within the app's quality limits. These PDFs remain over 10 MB:\n\n" +
                    string.Join("\n", oversized.Take(20).Select(f => $"{f.Name} — {f.Length / 1_000_000d:0.0} MB")) +
                    (oversized.Length > 20 ? $"\n…and {oversized.Length - 20} more." : "") +
                    "\n\nUse fewer pages or smaller source images if a smaller file is essential.", "GoodSort PDF", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (OperationCanceledException) { StatusText.Text = "Cancelled · no PDFs saved"; }
        catch (Exception ex)
        {
            StatusText.Text = "Could not complete · check the message and try again";
            if (prompt) MessageBox.Show(this, ex.Message, "GoodSort PDF", MessageBoxButton.OK, MessageBoxImage.Warning);
            else throw;
        }
        finally { cancellation?.Dispose(); cancellation = null; SetBusy(false); }
    }

    async Task PrintSavedAsync(IReadOnlyList<string> outputs, PrinterSelection printer, string savedStatus, bool prompt)
    {
        OpenFolderButton.Visibility = Visibility.Collapsed;
        Progress.IsIndeterminate = false;
        Progress.Maximum = outputs.Count; Progress.Value = 0;
        var progress = new Progress<PrintProgress>(p => { StatusText.Text = p.Message; Progress.Value = p.Submitted; });
        PrintResult result;
        try { result = await PrintSavedOutputs(outputs, printer, progress, cancellation!.Token); }
        catch (Exception ex) { result = new(0, outputs.Count, cancellation!.IsCancellationRequested, ex.Message); }
        StatusText.Text = result.Cancelled ? $"{savedStatus} · printing stopped ({result.Submitted} sent)"
            : result.Error != null ? $"{savedStatus} · print failed ({result.Submitted} sent)"
            : $"{savedStatus} · sent to printer";
        OpenFolderButton.Visibility = Visibility.Visible;
        if (result.Error != null && prompt)
            MessageBox.Show(this, $"All {outputs.Count} output PDFs are saved. Printing stopped after {result.Submitted} complete documents were sent.\n\n{result.Error}\n\nCheck the Windows print queue before retrying: jobs already sent, or part of the current document, may still print.",
                "Printing stopped", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    internal async Task OpenEditorAsync(string path, bool prompt = true)
    {
        if (cancellation != null) return;
        if (PageEditorControl.IsDirty && prompt && MessageBox.Show(this, "Replace the unsaved page arrangement with this document?", "Arrange pages", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        var origin = NewslettersPanel.Files.FirstOrDefault(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        ArrangeMode.IsChecked = true;
        cancellation = new CancellationTokenSource();
        SetBusy(true);
        Progress.IsIndeterminate = true;
        StatusText.Text = "Preparing pages…";
        try
        {
            await PageEditorControl.LoadAsync(path, cancellation.Token);
            editorOrigin = origin;
            ArrangedNameBox.Text = Path.GetFileNameWithoutExtension(path) + "_arranged";
            StatusText.Text = $"{PageEditorControl.KeptCount} page{(PageEditorControl.KeptCount == 1 ? "" : "s")} to save";
        }
        catch (OperationCanceledException) { StatusText.Text = "Loading cancelled"; }
        catch (Exception ex)
        {
            StatusText.Text = "Could not open this document";
            if (prompt) MessageBox.Show(this, ex.Message, "Arrange pages", MessageBoxButton.OK, MessageBoxImage.Warning);
            else throw;
        }
        finally { cancellation.Dispose(); cancellation = null; Progress.IsIndeterminate = false; SetBusy(false); }
    }

    internal async Task<string?> SaveArrangementAsync(bool prompt = true)
    {
        if (cancellation != null || PageEditorControl.PdfPath == null) return null;
        try
        {
            var fileName = DocumentPlan.CombinedFileName(ArrangedNameBox.Text, DateTime.Now);
            var folder = OutputFolder.Text;
            if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("Choose an output folder.");
            var source = PageEditorControl.PdfPath;
            var order = PageEditorControl.OutputOrder();
            var addToLists = AddArrangedCheckBox.IsChecked == true;
            var printer = PrintAfterSavingCheckBox.IsChecked == true ? SelectPrinter() : null;
            if (PrintAfterSavingCheckBox.IsChecked == true && printer == null) return null;
            SaveSettings();
            cancellation = new CancellationTokenSource();
            SetBusy(true); Progress.IsIndeterminate = true;
            var progress = new Progress<string>(message => StatusText.Text = message);
            var output = await Task.Run(() => new PageArrangementJob(new PdfSizeOptimizer(new WpfRasterCodec()))
                .Save(source, order, folder, fileName, cancellation.Token, message => ((IProgress<string>)progress).Report(message)));
            PageEditorControl.MarkSaved();
            var replaced = false;
            if (addToLists)
            {
                // Keep the same entry so order, group assignments and appended-file identity survive.
                // Combine all moves appended entries back to Notices when the editor is opened.
                if (editorOrigin != null && (NewslettersPanel.Files.Contains(editorOrigin) || NoticesPanel.Files.Contains(editorOrigin)))
                {
                    editorOrigin.ReplacePath(output);
                    replaced = true;
                }
                else
                {
                    NewslettersPanel.AddPaths([output]);
                    var added = NewslettersPanel.Files.First(f => f.Path.Equals(output, StringComparison.OrdinalIgnoreCase));
                    if (editorOrigin != null)
                    {
                        added.Year = editorOrigin.Year;
                        if (!string.IsNullOrWhiteSpace(editorOrigin.CustomGroup)) added.CustomGroup = editorOrigin.CustomGroup;
                    }
                    editorOrigin = added;
                }
            }
            lastOutput = folder;
            OpenFolderButton.Visibility = Visibility.Visible;
            StatusText.Text = replaced ? "Saved PDF · replaced original in other modes" : addToLists ? "Saved PDF · added to other modes" : "Saved PDF";
            if (new FileInfo(output).Length > PdfSizeOptimizer.TargetBytes) StatusText.Text += " · over 10 MB";
            if (printer != null) await PrintSavedAsync([output], printer, StatusText.Text, prompt);
            return output;
        }
        catch (OperationCanceledException) { StatusText.Text = "Cancelled · no PDF saved"; return null; }
        catch (Exception ex)
        {
            StatusText.Text = "Could not save · check the filename and folder";
            if (prompt) { MessageBox.Show(this, ex.Message, "Arrange pages", MessageBoxButton.OK, MessageBoxImage.Warning); return null; }
            throw;
        }
        finally { cancellation?.Dispose(); cancellation = null; Progress.IsIndeterminate = false; SetBusy(false); }
    }

    void SetBusy(bool busy)
    {
        ModeSelector.IsEnabled = NamingArea.IsEnabled = FileColumns.IsEnabled = PageEditorControl.IsEnabled = AddArrangedCheckBox.IsEnabled = PrintAfterSavingCheckBox.IsEnabled = OutputControls.IsEnabled = !busy;
        // Keep completion/error messages when re-enabling GO; Refresh would replace them.
        var status = StatusText.Text;
        if (busy) GoButton.IsEnabled = false;
        else { Refresh(); StatusText.Text = status; }
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.IsEnabled = true;
        Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (busy) { OpenFolderButton.Visibility = Visibility.Collapsed; Progress.Value = 0; }
    }
    void Cancel_Click(object sender, RoutedEventArgs e)
    {
        cancellation?.Cancel();
        CancelButton.IsEnabled = false;
        StatusText.Text = "Cancelling…";
    }
    void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (lastOutput != null) Process.Start(new ProcessStartInfo(lastOutput) { UseShellExecute = true });
    }
    async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (closeApproved) return;
        if (cancellation != null) { e.Cancel = true; Cancel_Click(this, new RoutedEventArgs()); return; }
        if (PageEditorControl.IsDirty && persistPreferences && MessageBox.Show(this, "Discard the unsaved page arrangement?", "Arrange pages", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
        { e.Cancel = true; return; }
        SaveSettings();
        e.Cancel = true;
        await PageEditorControl.DisposeAsync();
        closeApproved = true;
        _ = Dispatcher.BeginInvoke(new Action(Close));
    }
    void SaveSettings()
    {
        if (!ready || !persistPreferences) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(settingsPath, JsonSerializer.Serialize(new Preferences((int)TermBox.SelectedItem, (int)FirstWeekBox.SelectedItem, (int)LastWeekBox.SelectedItem, OutputFolder.Text, Mode, SuffixBox.Text, CombinedNameBox.Text, NewsletterSettings.Frequency, NewsletterSettings.CustomGroups, GroupNamesBox.Text, IssueBox.Text, AddArrangedCheckBox.IsChecked == true)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
    void Credit_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        try { Process.Start(new ProcessStartInfo(AppInfo.RepositoryUrl) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        { MessageBox.Show(this, $"Open this address in your browser:\n{AppInfo.RepositoryUrl}", "GoodSort PDF"); }
    }
    void About_Click(object sender, RoutedEventArgs e) => CreateAboutWindow().ShowDialog();

    internal Window CreateAboutWindow()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("THIRD-PARTY-NOTICES.txt"));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
        using var licenceReader = new StreamReader(assembly.GetManifestResourceStream("GoodSort.LICENSE")!);
        var licenceNotice = "Copyright © 2026 Andrew. Licensed under GNU GPL version 3 only. You may modify and redistribute this program under that licence. There is no warranty, to the extent permitted by law. The full licence appears below; source code is available through the GitHub link above.";
        var text = $"{AppInfo.Description}\n\n{licenceNotice}\n\n{AppInfo.ModeDescription}\n\nHOW IT BEGAN\n\n{AppInfo.OriginStory}\n\n{AppInfo.FreePromise}\n\nImages are fitted to A4 in their original portrait or landscape orientation, without cropping. PDFs are compressed towards 10 MB while retaining readable detail and hyperlinks. Very long or unusually complex files may remain larger; the app will tell you.\n\nCHANGE LOG\n\n{AppInfo.ChangeLog}\n\nWord and PowerPoint files use installed desktop Microsoft Office. All processing happens on this PC.\n\nText inside an image is not a clickable link.\n\nTHIRD-PARTY NOTICES\n\n" + reader.ReadToEnd() + "\n\nGOODSORT PDF LICENCE\n\n" + licenceReader.ReadToEnd();
        var panel = new DockPanel { Margin = new Thickness(20) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock { Text = $"GoodSort PDF {AppInfo.Version}", FontSize = 22, FontWeight = FontWeights.SemiBold });
        var credit = new TextBlock { Margin = new Thickness(0, 8, 0, 0) };
        var link = new Hyperlink(new Run(AppInfo.Credit)) { NavigateUri = new Uri(AppInfo.RepositoryUrl), ToolTip = "Open GoodSort PDF on GitHub" };
        link.RequestNavigate += Credit_RequestNavigate;
        credit.Inlines.Add(link); header.Children.Add(credit);
        DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
        panel.Children.Add(new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        return new Window { Owner = this, Title = "About GoodSort PDF", Width = 650, Height = 500, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (System.Windows.Media.Brush)FindResource("Paper"), Content = panel };
    }
}
