using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using NewsletterCombiner.Core;

namespace NewsletterCombiner.App;

public sealed class FileEntry(string path, bool isNewsletter) : INotifyPropertyChanged
{
    public string Path { get; private set; } = path;
    public void ReplacePath(string path)
    {
        // A renamed output must retain the source group instead of inferring a new one.
        if (!string.IsNullOrWhiteSpace(customGroup)) customGroupEdited = true;
        Path = System.IO.Path.GetFullPath(path);
        foreach (var property in new[] { nameof(Path), nameof(Name), nameof(Folder), nameof(IsPdf), nameof(Extension) })
            PropertyChanged?.Invoke(this, new(property));
    }
    public string Name => System.IO.Path.GetFileName(Path);
    public string Folder => System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(Path)) ?? "";
    public bool IsPdf => System.IO.Path.GetExtension(Path).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
    public string Extension => System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant();
    bool showYear = isNewsletter;
    public Visibility YearVisibility => showYear ? Visibility.Visible : Visibility.Collapsed;
    public void SetYearVisible(bool visible)
    {
        showYear = visible;
        if (visible && string.IsNullOrWhiteSpace(Year)) Year = NewsletterPlan.GuessYear(Path);
        foreach (var property in new[] { nameof(YearVisibility), nameof(SchoolYearVisibility), nameof(CustomGroupVisibility) })
            PropertyChanged?.Invoke(this, new(property));
    }
    bool customGroups;
    bool configuringGroups;
    bool customGroupEdited;
    string customGroup = "";
    public IReadOnlyList<string> GroupChoices { get; private set; } = [];
    public Visibility SchoolYearVisibility => showYear && !customGroups ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CustomGroupVisibility => showYear && customGroups ? Visibility.Visible : Visibility.Collapsed;
    public string CustomGroup
    {
        get => customGroup;
        set
        {
            if (configuringGroups || customGroup == value) return;
            customGroupEdited = true;
            customGroup = value;
            PropertyChanged?.Invoke(this, new(nameof(CustomGroup)));
            PropertyChanged?.Invoke(this, new(nameof(Assignment)));
        }
    }
    public string Assignment { get => customGroups ? CustomGroup : Year; set { if (customGroups) CustomGroup = value; else Year = value; } }
    public void ConfigureGrouping(bool custom, IReadOnlyList<string> groups)
    {
        configuringGroups = true;
        try
        {
            customGroups = custom;
            if (custom && !customGroupEdited) customGroup = NewsletterPlan.GuessGroup(Path, groups);
            if (!GroupChoices.SequenceEqual(groups))
            {
                GroupChoices = groups;
                PropertyChanged?.Invoke(this, new(nameof(GroupChoices)));
            }
            foreach (var property in new[] { nameof(CustomGroup), nameof(SchoolYearVisibility), nameof(CustomGroupVisibility), nameof(Assignment) })
                PropertyChanged?.Invoke(this, new(property));
        }
        finally { configuringGroups = false; }
    }
    bool isAppended;
    public bool IsAppended
    {
        get => isAppended;
        set
        {
            isAppended = value;
            PropertyChanged?.Invoke(this, new(nameof(RowBackground)));
            PropertyChanged?.Invoke(this, new(nameof(AppendedVisibility)));
        }
    }
    public string RowBackground => isAppended ? "#FCEEF2" : "#F4F8FC";
    public Visibility AppendedVisibility => isAppended ? Visibility.Visible : Visibility.Collapsed;
    string year = isNewsletter ? NewsletterPlan.GuessYear(path) : "";
    public string Year { get => year; set { year = value; PropertyChanged?.Invoke(this, new(nameof(Year))); PropertyChanged?.Invoke(this, new(nameof(Assignment))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class FilePanel : UserControl
{
    public static readonly DependencyProperty HeadingProperty = DependencyProperty.Register(nameof(Heading), typeof(string), typeof(FilePanel));
    public static readonly DependencyProperty SubheadingProperty = DependencyProperty.Register(nameof(Subheading), typeof(string), typeof(FilePanel));
    public string Heading { get => (string)GetValue(HeadingProperty); set => SetValue(HeadingProperty, value); }
    public string Subheading { get => (string)GetValue(SubheadingProperty); set => SetValue(SubheadingProperty, value); }
    bool isNewsletter;
    public bool IsNewsletter
    {
        get => isNewsletter;
        set
        {
            if (isNewsletter == value) return;
            isNewsletter = value;
            foreach (var file in Files) file.SetYearVisible(value);
            Refresh();
        }
    }
    bool customGroups;
    IReadOnlyList<string> groupNames = [];
    public void ConfigureGrouping(bool custom, IReadOnlyList<string> groups)
    {
        customGroups = custom;
        groupNames = groups;
        foreach (var file in Files) file.ConfigureGrouping(custom, groups);
        YearLabel.Text = custom ? "GROUP" : "YEAR LEVEL";
    }
    readonly FileEntryCollection files = [];
    public ObservableCollection<FileEntry> Files => files;
    public event EventHandler? Changed;
    public bool CanArrangePages { get; set; }
    public event Action<string>? ArrangeRequested;

    readonly HashSet<FileEntry> observed = [];
    void EntryChanged(object? sender, PropertyChangedEventArgs e) => Changed?.Invoke(this, EventArgs.Empty);

    public FilePanel()
    {
        InitializeComponent();
        FileList.ItemsSource = Files;
        Files.CollectionChanged += (_, change) =>
        {
            if (change.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var entry in observed) entry.PropertyChanged -= EntryChanged;
                observed.Clear();
                foreach (var entry in Files) Observe(entry);
            }
            else
            {
                if (change.Action != NotifyCollectionChangedAction.Move && change.OldItems != null)
                    foreach (FileEntry entry in change.OldItems)
                    { entry.PropertyChanged -= EntryChanged; observed.Remove(entry); }
                if (change.Action != NotifyCollectionChangedAction.Move && change.NewItems != null)
                    foreach (FileEntry entry in change.NewItems) Observe(entry);
            }
            Refresh();
        };
        Loaded += (_, _) => Refresh();
    }

    void Observe(FileEntry entry)
    {
        entry.SetYearVisible(IsNewsletter);
        entry.ConfigureGrouping(customGroups, groupNames);
        if (observed.Add(entry)) entry.PropertyChanged += EntryChanged;
    }

    void Refresh()
    {
        EmptyState.Visibility = Files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CountLabel.Text = $"{Files.Count} FILE{(Files.Count == 1 ? "" : "S")}";
        YearLabel.Visibility = IsNewsletter ? Visibility.Visible : Visibility.Collapsed;
        ClearButton.IsEnabled = Files.Count > 0;
        RefreshSelection();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    void RefreshSelection()
    {
        var single = FileList.SelectedItems.Count == 1;
        UpButton.IsEnabled = single && FileList.SelectedIndex > 0;
        DownButton.IsEnabled = single && FileList.SelectedIndex < Files.Count - 1;
        RemoveButton.IsEnabled = FileList.SelectedItems.Count > 0;
    }

    void Selection_Changed(object sender, SelectionChangedEventArgs e) => RefreshSelection();
    void File_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (!CanArrangePages || ((FrameworkElement)sender).DataContext is not FileEntry { IsPdf: true }) e.Handled = true;
    }
    void ArrangePages_Click(object sender, RoutedEventArgs e)
    {
        if (CanArrangePages && ((FrameworkElement)sender).DataContext is FileEntry { IsPdf: true } file) ArrangeRequested?.Invoke(file.Path);
    }
    void Add_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Multiselect = true, Title = $"Add {Heading.ToLowerInvariant()}", Filter = "Supported documents|*.pdf;*.doc;*.docx;*.rtf;*.ppt;*.pptx;*.jpg;*.jpeg;*.png;*.bmp" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) AddPaths(dialog.FileNames);
    }

    public void AddPaths(IEnumerable<string> paths)
    {
        var skipped = new List<string>();
        var known = Files.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = new List<FileEntry>();
        foreach (var path in paths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = false, AttributesToSkip = FileAttributes.ReparsePoint };
                    foreach (var file in Directory.EnumerateFiles(path, "*", options).OrderBy(p => p, StringComparer.OrdinalIgnoreCase)) AddOne(file, skipped, known, added);
                }
                else AddOne(path, skipped, known, added);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                skipped.Add($"{System.IO.Path.GetFileName(path)}: {e.Message}");
            }
        }
        files.AddRange(added);
        if (skipped.Count > 0)
            MessageBox.Show(Window.GetWindow(this), "Not added:\n" + string.Join("\n", skipped.Take(8)) + (skipped.Count > 8 ? $"\n…and {skipped.Count - 8} more." : ""), "GoodSort PDF", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    void AddOne(string path, List<string> skipped, HashSet<string> known, List<FileEntry> added)
    {
        if (System.IO.Path.GetFileName(path).StartsWith("~$")) return;
        if (!File.Exists(path) || !NewsletterPlan.Extensions.Contains(System.IO.Path.GetExtension(path))) { skipped.Add(System.IO.Path.GetFileName(path)); return; }
        path = System.IO.Path.GetFullPath(path);
        if (!known.Add(path)) return;
        added.Add(new FileEntry(path, IsNewsletter));
    }

    void Files_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        DropFrame.BorderBrush = (Brush)FindResource("Accent");
        DropFrame.Background = (Brush)FindResource("Tint");
        e.Handled = true;
    }
    void ResetDrop() { DropFrame.BorderBrush = (Brush)FindResource("Line"); DropFrame.Background = Brushes.White; }
    void Files_DragLeave(object sender, DragEventArgs e) => ResetDrop();
    void Files_Drop(object sender, DragEventArgs e)
    {
        ResetDrop();
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) AddPaths(paths);
        e.Handled = true;
    }
    void Up_Click(object sender, RoutedEventArgs e) => Move(-1);
    void Down_Click(object sender, RoutedEventArgs e) => Move(1);
    void Move(int direction)
    {
        var index = FileList.SelectedIndex;
        if (index < 0 || index + direction < 0 || index + direction >= Files.Count) return;
        var selected = Files[index];
        Files.Move(index, index + direction);
        FileList.SelectedItem = selected;
        FileList.ScrollIntoView(selected);
        RefreshSelection();
    }
    void Remove_Click(object sender, RoutedEventArgs e) => RemoveSelected();
    void RemoveSelected() { foreach (var file in FileList.SelectedItems.Cast<FileEntry>().ToArray()) Files.Remove(file); }
    void Clear_Click(object sender, RoutedEventArgs e) => Files.Clear();
    void FileList_KeyDown(object sender, KeyEventArgs e)
    {
        // Let editable year fields handle their own Delete key.
        if (e.OriginalSource is TextBox) return;
        if (e.Key == Key.Delete) { RemoveSelected(); e.Handled = true; }
    }
}

// A folder drop updates bindings once, rather than once per file.
internal sealed class FileEntryCollection : ObservableCollection<FileEntry>
{
    public void AddRange(IReadOnlyList<FileEntry> added)
    {
        if (added.Count == 0) return;
        CheckReentrancy();
        foreach (var entry in added) Items.Add(entry);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
