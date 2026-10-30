using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Imaging;

namespace NewsletterCombiner.App;

public sealed class PageTile(int sourceIndex, bool copy = false) : INotifyPropertyChanged
{
    public int SourceIndex { get; } = sourceIndex;
    public bool IsCopy { get; } = copy;
    public string SourceLabel => $"Original page {SourceIndex + 1}" + (IsCopy ? " · copy" : "");
    int position;
    public int Position { get => position; internal set { position = value; Notify(nameof(Caption)); } }
    public string Caption => IsRemoved ? $"{Position} · Removed" : $"{Position}";
    bool removed;
    public bool IsRemoved { get => removed; internal set { removed = value; Notify(nameof(IsRemoved)); Notify(nameof(Caption)); Notify(nameof(RemoveMenu)); } }
    public string RemoveMenu => IsRemoved ? "Undelete page" : "Remove page";
    BitmapSource? preview;
    public BitmapSource? Preview { get => preview; internal set { preview = value; Notify(nameof(Preview)); Notify(nameof(PreviewPlaceholder)); } }
    public Visibility PreviewPlaceholder => Preview == null ? Visibility.Visible : Visibility.Collapsed;
    string previewText = "Loading preview…";
    public string PreviewText { get => previewText; internal set { previewText = value; Notify(nameof(PreviewText)); } }
    bool dropTarget;
    public bool IsDropTarget { get => dropTarget; internal set { dropTarget = value; Notify(nameof(IsDropTarget)); } }
    bool dropAfter;
    public bool DropAfter { get => dropAfter; internal set { dropAfter = value; Notify(nameof(DropAfter)); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    void Notify(string name) => PropertyChanged?.Invoke(this, new(name));
}

public sealed record PageRow(IReadOnlyList<PageTile> Pages);
