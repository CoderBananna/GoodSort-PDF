using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace NewsletterCombiner.App;

public partial class PageEditor : UserControl
{
    readonly List<PageTile> pages = [];
    readonly Dictionary<FrameworkElement, (PageTile Page, CancellationTokenSource Cancel)> requests = [];
    readonly HashSet<Task> pending = [];
    PdfPreviewSession? session;
    Point dragStart;
    PageTile? dragged;
    PageTile? dropTarget;
    DateTime lastScroll;
    const string PageDragFormat = "GoodSort.PDF.Page";
    public IReadOnlyList<PageTile> Pages => pages;
    public string? PdfPath => session?.PdfPath;
    public string? SourcePath => session?.SourcePath;
    public bool IsDirty { get; private set; }
    public int KeptCount => pages.Count(p => !p.IsRemoved);
    internal PdfPreviewSession? Session => session;
    public event EventHandler? Changed;
    public event Action<string>? OpenRequested;
    public PageEditor()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) StopPreviews();
            else Dispatcher.BeginInvoke(() => RestartVisiblePreviews(this));
        };
    }

    public async Task LoadAsync(string path, CancellationToken token)
    {
        var next = await PdfPreviewSession.OpenAsync(path, token);
        if (token.IsCancellationRequested) { await next.DisposeAsync(); token.ThrowIfCancellationRequested(); }
        StopPreviews();
        var previous = session;
        session = next;
        pages.Clear();
        for (var i = 0; i < next.PageCount; i++) pages.Add(new(i));
        IsDirty = false;
        DocumentTitle.Text = Path.GetFileName(path);
        DocumentTitle.ToolTip = path;
        RebuildRows();
        if (previous != null) await previous.DisposeAsync();
    }

    public void MarkSaved() { IsDirty = false; UpdateSummary(); }
    public int[] OutputOrder() => pages.Where(p => !p.IsRemoved).Select(p => p.SourceIndex).ToArray();
    public void Duplicate(int index)
    {
        if (index < 0 || index >= pages.Count) return;
        pages.Insert(index + 1, new PageTile(pages[index].SourceIndex, true));
        IsDirty = true;
        RebuildRows();
    }
    public void ToggleRemoved(int index)
    {
        if (index < 0 || index >= pages.Count) return;
        pages[index].IsRemoved = !pages[index].IsRemoved;
        IsDirty = true;
        UpdateSummary();
    }
    public void MovePage(int from, int to)
    {
        if (from < 0 || from >= pages.Count || to < 0 || to >= pages.Count || from == to) return;
        var page = pages[from]; pages.RemoveAt(from); pages.Insert(to, page);
        IsDirty = true;
        RebuildRows();
    }
    void RebuildRows()
    {
        for (var i = 0; i < pages.Count; i++) pages[i].Position = i + 1;
        PageRows.ItemsSource = pages.Chunk(4).Select(chunk => new PageRow(chunk)).ToArray();
        EmptyState.Visibility = pages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSummary();
    }
    void UpdateSummary()
    {
        PageSummary.Text = pages.Count == 0 ? "Drop a document or PDF to begin" : $"{KeptCount} pages to save · {pages.Count - KeptCount} removed · drag to reorder";
        Changed?.Invoke(this, EventArgs.Empty);
    }
    void Choose_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Arrange pages", Filter = "Supported documents|*.pdf;*.doc;*.docx;*.rtf;*.ppt;*.pptx;*.jpg;*.jpeg;*.png;*.bmp" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) OpenRequested?.Invoke(dialog.FileName);
    }
    void Duplicate_Click(object sender, RoutedEventArgs e) { if (((FrameworkElement)sender).DataContext is PageTile page) Duplicate(pages.IndexOf(page)); }
    void Remove_Click(object sender, RoutedEventArgs e) { if (((FrameworkElement)sender).DataContext is PageTile page) ToggleRemoved(pages.IndexOf(page)); }
    void Tile_KeyDown(object sender, KeyEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not PageTile page) return;
        var index = pages.IndexOf(page);
        if (e.Key == Key.Delete) { ToggleRemoved(index); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.Left or Key.Right)
        { MovePage(index, index + (e.Key == Key.Left ? -1 : 1)); e.Handled = true; }
    }
    void Tile_MouseDown(object sender, MouseButtonEventArgs e)
    {
        dragStart = e.GetPosition(this);
        dragged = ((FrameworkElement)sender).DataContext as PageTile;
        ((FrameworkElement)sender).Focus();
    }
    void Tile_MouseMove(object sender, MouseEventArgs e)
    {
        if (dragged == null || e.LeftButton != MouseButtonState.Pressed) return;
        var position = e.GetPosition(this);
        if (Math.Abs(position.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(position.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        try { DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(PageDragFormat, dragged), DragDropEffects.Move); }
        finally { dragged = null; ClearDropTarget(); }
    }
    void ClearDropTarget() { if (dropTarget != null) dropTarget.IsDropTarget = false; dropTarget = null; }
    static FrameworkElement? TileAt(DependencyObject? element)
    {
        while (element != null)
        {
            if (element is FrameworkElement { DataContext: PageTile } tile && tile.Focusable) return tile;
            element = element is Visual ? VisualTreeHelper.GetParent(element) : null;
        }
        return null;
    }
    void Editor_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(PageDragFormat) is PageTile page && pages.Contains(page))
        {
            e.Effects = DragDropEffects.Move;
            ClearDropTarget();
            if (TileAt(e.OriginalSource as DependencyObject) is { } tile && tile.DataContext is PageTile target)
            {
                dropTarget = target; target.IsDropTarget = true;
                target.DropAfter = e.GetPosition(tile).X > tile.ActualWidth / 2;
            }
            var pointer = e.GetPosition(PageRows);
            if (DateTime.UtcNow - lastScroll > TimeSpan.FromMilliseconds(45) && FindScroll(PageRows) is { } scroll)
            {
                if (pointer.Y < 45) { scroll.ScrollToVerticalOffset(Math.Max(0, scroll.VerticalOffset - 40)); lastScroll = DateTime.UtcNow; }
                else if (pointer.Y > PageRows.ActualHeight - 45) { scroll.ScrollToVerticalOffset(scroll.VerticalOffset + 40); lastScroll = DateTime.UtcNow; }
            }
        }
        else e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    void Editor_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(PageDragFormat) is PageTile page && pages.Contains(page))
        {
            var from = pages.IndexOf(page);
            var insertion = pages.Count;
            if (TileAt(e.OriginalSource as DependencyObject) is { } tile && tile.DataContext is PageTile target)
                insertion = pages.IndexOf(target) + (e.GetPosition(tile).X > tile.ActualWidth / 2 ? 1 : 0);
            if (insertion > from) insertion--;
            MovePage(from, Math.Clamp(insertion, 0, pages.Count - 1));
            ClearDropTarget();
        }
        else if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            if (files.Length != 1 || Directory.Exists(files[0])) MessageBox.Show(Window.GetWindow(this), "Drop one document at a time.", "Arrange pages");
            else OpenRequested?.Invoke(files[0]);
        }
        e.Handled = true;
    }
    static ScrollViewer? FindScroll(DependencyObject element)
    {
        if (element is ScrollViewer scroll) return scroll;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            if (FindScroll(VisualTreeHelper.GetChild(element, i)) is { } found) return found;
        return null;
    }
    void Tile_Loaded(object sender, RoutedEventArgs e) => RequestPreview((FrameworkElement)sender);
    void Tile_Unloaded(object sender, RoutedEventArgs e) => StopPreview((FrameworkElement)sender);
    void Tile_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        var tile = (FrameworkElement)sender;
        if (tile.IsLoaded) RequestPreview(tile);
    }
    void RequestPreview(FrameworkElement tile)
    {
        StopPreview(tile);
        if (!IsVisible || session == null || tile.DataContext is not PageTile page) return;
        var cancel = new CancellationTokenSource();
        requests[tile] = (page, cancel);
        page.PreviewText = "Loading preview…";
        var task = CompletePreviewAsync(tile, page, session, cancel.Token);
        pending.Add(task);
        _ = RemovePendingAsync(task);
    }
    async Task RemovePendingAsync(Task task) { await task; pending.Remove(task); }
    async Task CompletePreviewAsync(FrameworkElement tile, PageTile page, PdfPreviewSession source, CancellationToken token)
    {
        try
        {
            var image = await source.ThumbnailAsync(page.SourceIndex, token);
            if (!token.IsCancellationRequested && requests.TryGetValue(tile, out var request) && request.Page == page) page.Preview = image;
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!token.IsCancellationRequested) page.PreviewText = "Preview unavailable"; }
    }
    void StopPreview(FrameworkElement tile)
    {
        if (!requests.Remove(tile, out var old)) return;
        old.Cancel.Cancel(); old.Cancel.Dispose(); old.Page.Preview = null;
    }
    void StopPreviews() { foreach (var tile in requests.Keys.ToArray()) StopPreview(tile); }
    void RestartVisiblePreviews(DependencyObject element)
    {
        if (element is FrameworkElement { DataContext: PageTile, Focusable: true } tile) RequestPreview(tile);
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) RestartVisiblePreviews(VisualTreeHelper.GetChild(element, i));
    }
    internal async Task WaitForPreviewsAsync() { while (pending.Count > 0) { await Task.WhenAll(pending.ToArray()); pending.RemoveWhere(task => task.IsCompleted); } }
    public async Task DisposeAsync()
    {
        StopPreviews();
        var old = session; session = null;
        pages.Clear(); PageRows.ItemsSource = null;
        if (old != null) await old.DisposeAsync();
    }
}
