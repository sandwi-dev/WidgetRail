using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;
using Windows.System;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

public sealed partial class FileBrowserRow : INotifyPropertyChanged
{
    internal FileBrowserRow(FileBrowserItem entry, bool multi, bool selected, bool reviewing)
    {
        Entry = entry;
        Checked = selected;
        CheckVisibility = multi ? Visibility.Visible : Visibility.Collapsed;
        Detail = reviewing ? Path.GetDirectoryName(entry.Path) ?? "" : entry.Folder ? "" : entry.Length >= 1048576
            ? $"{entry.Length / 1048576d:0.#} MB" : $"{Math.Ceiling(entry.Length / 1024d):0} KB";
    }
    internal FileBrowserItem Entry
    {
        get;
    }
    public string Name => Entry.Name;
    public string Glyph => Entry.Folder ? "\uE8B7" : "\uE8A5";
    public string Detail
    {
        get;
    }
    public Visibility CheckVisibility
    {
        get;
    }
    public double CheckOpacity => Entry.Folder ? 0 : 1;
    internal string AccessibleName => Entry.Name + (Entry.Folder ? ", folder" : CheckVisibility == Visibility.Visible ? Checked ? ", selected" : ", not selected" : "");
    public bool Checked
    {
        get; private set;
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void Mark(bool value)
    {
        if (Checked == value)
            return;
        Checked = value;
        PropertyChanged?.Invoke(this, new(nameof(Checked)));
    }
}

internal sealed partial class FileBrowserDialog
{
    private FileBrowserRow[] rows = [];
    private bool HeldSelect;
    private static bool Down(VirtualKey key) => (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
    private FileBrowserRow? RowAt(DependencyObject? source)
    {
        for (var node = source; node is not null && !ReferenceEquals(files, node); node = VisualTreeHelper.GetParent(node))
            if (node is ListViewItem container && files.ItemFromContainer(container) is FileBrowserRow row)
                return row;
        return null;
    }
    private FileBrowserRow? FocusedRow() => XamlRoot is null ? null : RowAt(FocusManager.GetFocusedElement(XamlRoot) as DependencyObject);
    private void PresentRows(IEnumerable<FileBrowserItem> entries, string? focus, bool enter)
    {
        rows = entries.Select(item => new FileBrowserRow(item, request.Mode == FileBrowserMode.Multiple, picked.ContainsKey(item.Path), reviewing)).ToArray();
        files.ItemsSource = rows;
        if (enter)
            FocusRow(rows.FirstOrDefault(row => string.Equals(row.Entry.Path, focus, StringComparison.OrdinalIgnoreCase)) ?? rows.FirstOrDefault());
    }
    private void FocusRow(FileBrowserRow? row)
    {
        if (row is null)
        {
            (DialogTemplatePart("CloseButton") as Control)?.Focus(FocusState.Keyboard);
            return;
        }
        files.ScrollIntoView(row);
        files.UpdateLayout();
        var entries = rows;
        var focused = FocusManager.GetFocusedElement(XamlRoot);
        if (files.ContainerFromItem(row) is not Control item || !item.Focus(FocusState.Keyboard))
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (closed || !ReferenceEquals(rows, entries) || selectedPath != row.Entry.Path ||
                    !ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), focused))
                    return;
                files.ScrollIntoView(row);
                files.UpdateLayout();
                (files.ContainerFromItem(row) as Control)?.Focus(FocusState.Keyboard);
            });
        selectedPath = row.Entry.Path;
        UpdateActions();
    }
    private void Point(FileBrowserRow row)
    {
        if (reading is not null || committing)
            return;
        ResetConfirmation();
        selectedPath = row.Entry.Path;
        if (request.Mode == FileBrowserMode.Multiple && !row.Entry.Folder)
        {
            if (Down(VirtualKey.Shift))
                MarkRange(row);
            else
            {
                Mark(row, !row.Checked);
                anchor = row.Entry.Path;
            }
        }
        if (request.Mode == FileBrowserMode.Save && !row.Entry.Folder)
            filename.Text = row.Name;
        UpdateActions();
    }
    private void Mark(FileBrowserRow row, bool value)
    {
        if (row.Entry.Folder)
            return;
        if (value && !picked.ContainsKey(row.Entry.Path) && picked.Count >= request.Limit)
        {
            Message($"Choose up to {request.Limit} files.");
            return;
        }
        if (value)
            picked[row.Entry.Path] = row.Name;
        else
            picked.Remove(row.Entry.Path);
        row.Mark(value);
        if (files.ContainerFromItem(row) is ListViewItem container)
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(container, row.AccessibleName);
        UpdateActions();
    }
    private void MarkRange(FileBrowserRow end)
    {
        var first = Array.FindIndex(rows, r => r.Entry.Path == anchor);
        var last = Array.IndexOf(rows, end);
        if (first < 0 || last < 0)
        {
            Mark(end, true);
            anchor = end.Entry.Path;
            return;
        }
        for (var i = Math.Min(first, last); i <= Math.Max(first, last); i++)
            Mark(rows[i], true);
    }
    private void Activate(FileBrowserRow row)
    {
        if (committing || reading is not null)
            return;
        if (row.Entry.Folder)
        {
            Go(row.Entry.Path);
            return;
        }
        selectedPath = row.Entry.Path;
        if (request.Mode == FileBrowserMode.Multiple)
        {
            Mark(row, !row.Checked);
            anchor = row.Entry.Path;
        }
        else
        {
            if (request.Mode == FileBrowserMode.Save)
                filename.Text = row.Name;
            _ = CommitAsync();
        }
    }
    private void GoToParent()
    {
        if (page is { } current && Path.GetDirectoryName(current.Directory) is { } parent)
            _ = NavigateAsync(parent, "", null, current.Directory, true, true);
    }
    private void Back()
    {
        if (enteringLocation && page is not null)
        {
            ShowLocation(page.Directory);
            FocusRow(rows.FirstOrDefault());
            return;
        }
        if (confirming)
        {
            ResetConfirmation();
            Message("");
            return;
        }
        if (reviewing)
        {
            ToggleReview();
            return;
        }
        if (showingPlaces)
        {
            showingPlaces = false;
            LayoutPicker();
            FocusRow(rows.FirstOrDefault());
            return;
        }
        if (history.Count == 0)
        {
            Hide();
            return;
        }
        var visit = history[^1];
        _ = NavigateAsync(visit.Path, visit.Query, visit.Cursor, visit.Focus, false, true, offset: visit.Offset, returnTo: visit);
    }
    private void Page(int delta)
    {
        if (page is null || reading is not null || reviewing)
            return;
        FileBrowserItem? after;
        if (delta < 0 && pages.Count != 0)
            after = pages.Peek();
        else if (delta > 0 && page.HasMore)
            after = page.Items[^1];
        else
            return;
        _ = NavigateAsync(page.Directory, appliedQuery, after, null, false, true, pageStep: delta);
    }
    private void ToggleReview()
    {
        if (page is null || reading is not null)
            return;
        reviewing = !reviewing;
        if (reviewing)
            PresentRows(picked.Select(pair => new FileBrowserItem(pair.Key, pair.Value, false)), null, true);
        else
            PresentRows(page.Items, selectedPath, true);
        Message("");
        empty.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        empty.Text = reviewing ? "No files selected." : appliedQuery.Length == 0 ? "No matching files." : "No matching items in this folder.";
        LayoutPicker();
        UpdateActions();
    }
    private void UpdateActions()
    {
        review.Visibility = request.Mode == FileBrowserMode.Multiple ? Visibility.Visible : Visibility.Collapsed;
        review.Content = reviewing ? "Folder contents" : $"Selected ({picked.Count})";
        clear.Visibility = reviewing && picked.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        previous.Visibility = !reviewing && pages.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        next.Visibility = !reviewing && page?.HasMore == true ? Visibility.Visible : Visibility.Collapsed;
        footer.Visibility = request.Mode == FileBrowserMode.Multiple || previous.Visibility == Visibility.Visible ||
            next.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
        up.IsEnabled = !reviewing && page is not null && Path.GetDirectoryName(page.Directory) is not null;
        IsPrimaryButtonEnabled = !committing && reading is null && page is not null && request.Mode switch
        {
            FileBrowserMode.Multiple => picked.Count > 0,
            FileBrowserMode.Open => rows.Any(row => !row.Entry.Folder && row.Entry.Path == selectedPath),
            FileBrowserMode.Save => filename.Text.Trim().Length != 0,
            _ => true
        };
        if (!confirming)
            PrimaryButtonText = request.Mode == FileBrowserMode.Multiple && picked.Count > 0
            ? $"{request.CommitLabel} ({picked.Count})" : request.CommitLabel;
    }

    private async Task CommitAsync()
    {
        if (committing || reading is not null || page is not { } current)
            return;
        var token = lifetime.Token;
        committing = true;
        editor.IsEnabled = false;
        UpdateActions();
        try
        {
            var paths = request.Mode switch
            {
                FileBrowserMode.Multiple => picked.Keys.ToArray(),
                FileBrowserMode.Folder => new[] { current.Directory },
                FileBrowserMode.Save => new[] { FileBrowserDirectory.SavePath(current.Directory, filename.Text, request.Extension) },
                _ => selectedPath is null ? Array.Empty<string>() : new[] { selectedPath }
            };
            if (paths.Length == 0 || paths.Length > request.Limit)
                return;
            var existing = await FileBrowserDirectory.InspectAsync(() =>
            {
                (DateTime Timestamp, long Size)? version = null;
                foreach (var path in paths)
                {
                    token.ThrowIfCancellationRequested();
                    if (request.Mode == FileBrowserMode.Folder)
                    {
                        if (!Directory.Exists(path))
                            throw new DirectoryNotFoundException();
                    }
                    else if (request.Mode == FileBrowserMode.Save)
                    {
                        if (!Directory.Exists(current.Directory))
                            throw new DirectoryNotFoundException();
                        try
                        {
                            if ((File.GetAttributes(path) & FileAttributes.Directory) != 0)
                                throw new IOException("A directory already uses that name.");
                            var info = new FileInfo(path);
                            version = (info.LastWriteTimeUtc, info.Length);
                        }
                        catch (FileNotFoundException) { /* A new filename is valid. */ }
                    }
                    else if (!File.Exists(path) || !FileBrowserDirectory.MatchesType(path, request.Extension))
                        throw new FileNotFoundException();
                }
                return version;
            }, token);
            token.ThrowIfCancellationRequested();
            if (request.Mode == FileBrowserMode.Save && existing is not null && (!confirming || replacePath != paths[0] || replaceVersion != existing))
            {
                confirming = true;
                replacePath = paths[0];
                replaceVersion = existing;
                PrimaryButtonText = "Replace file";
                Message($"“{Path.GetFileName(paths[0])}” already exists. Replace it?");
                FocusRow(null);
                (DialogTemplatePart("CloseButton") as Control)?.Focus(FocusState.Keyboard);
                return;
            }
            Result = paths;
            Hide();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (FileError(error))
        {
            Message(error is ArgumentException ? error.Message : "The selection is no longer available. Review the files or choose another location.");
        }
        finally { committing = false; if (!closed) { editor.IsEnabled = true; UpdateActions(); } }
    }
}
