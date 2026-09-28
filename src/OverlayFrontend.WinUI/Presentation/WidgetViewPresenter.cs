using PresentationSession = WidgetRail.WidgetPresentationSession.WidgetPresentationSession;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed record WidgetElementIdentity(string Scope, string Id, ViewNodeKind Kind, string ItemPath);
internal sealed record WidgetActionRequest(WidgetPresentationAuthority Authority, WidgetActionEvent Action);

/// <summary>
/// Trusted declaration-to-control adapter. WinUI owns all layout, focus traversal,
/// control behavior and painting. This adapter retains controls by semantic identity
/// and reconciles declaration membership; it contains no geometry or frame scheduler.
/// </summary>
internal sealed partial class WidgetViewPresenter : ContentControl, IAsyncDisposable
{
    private sealed record Declaration(ViewNode Node, WidgetElementIdentity Identity, string? ParentId);
    private sealed record Binding(WidgetElementIdentity Identity, FrameworkElement Element, Panel? Children, object Token);
    private Dictionary<string, Declaration> declarations = new(StringComparer.Ordinal);
    private Dictionary<string, Binding> bindings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WidgetElementIdentity> remembered = new(StringComparer.Ordinal);
    private WidgetPresentationFrame? frame;
    private bool applying;
    private bool needsEntry;
    private bool focusQueued;
    private bool restoreNativeFocus;
    private Binding? pendingRestore;
    private long actionSequence;
    private readonly bool presentationOnly;
    public PresentationSession? Session { get; set; }
    public Func<WidgetActionRequest, Task>? DispatchActionAsync { get; set; }

    /// <summary>Explicit page/window entry. Ordinary data updates do not call this.</summary>
    public void Enter(bool restoreNativeFocus = false)
    {
        this.restoreNativeFocus |= restoreNativeFocus;
        needsEntry = needsEntry || FocusedBinding() is not { } focused || !Eligible(focused);
        QueueEntryFocus();
    }

    public WidgetViewPresenter(bool presentationOnly = false)
    {
        this.presentationOnly = presentationOnly;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        IsTabStop = false;
        XYFocusKeyboardNavigation = XYFocusKeyboardNavigationMode.Enabled;
        Loaded += (_, _) => QueueEntryFocus();
        GotFocus += (_, _) => RememberFocus();
        GettingFocus += OnGettingFocus;
        AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => CancelGroupEntry()), true);
        AddHandler(KeyDownEvent, new KeyEventHandler((_, args) =>
        {
            if (args.Key is Windows.System.VirtualKey.Up or Windows.System.VirtualKey.Down or Windows.System.VirtualKey.Left or
                Windows.System.VirtualKey.Right or Windows.System.VirtualKey.Tab or Windows.System.VirtualKey.Home or
                Windows.System.VirtualKey.End or Windows.System.VirtualKey.PageUp or Windows.System.VirtualKey.PageDown or
                Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space or Windows.System.VirtualKey.Escape) CancelGroupEntry();
        }), true);
    }

    public void Apply(WidgetPresentationFrame next) => ApplyCore(next, next.Snapshot.Root,
        next.Snapshot.Root.InputScopeId ?? next.Snapshot.Root.Id);

    internal void ApplyFragment(WidgetPresentationFrame parent, ViewNode root, string scope) => ApplyCore(parent, root, scope);

    private void ApplyCore(WidgetPresentationFrame next, ViewNode root, string rootScope)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!DispatcherQueue.HasThreadAccess) throw new InvalidOperationException("Widget presentation must use its WinUI dispatcher.");
        ArgumentNullException.ThrowIfNull(next);
        var sameOwner = frame is { } prior && SameOwner(prior.Authority, next.Authority);
        if (!presentationOnly && sameOwner && frame!.Authority.SnapshotSequence >= next.Authority.SnapshotSequence) return;
        var plan = Plan(root, rootScope);
        var nextBindings = new Dictionary<string, Binding>(StringComparer.Ordinal);
        foreach (var declaration in plan.Values)
            nextBindings.Add(declaration.Node.Id, sameOwner && bindings.TryGetValue(declaration.Node.Id, out var retained)
                && retained.Identity == declaration.Identity ? retained : Create(declaration));

        var focused = FocusedBinding();
        var oldScope = frame?.Authority.ActiveInputScopeId;
        applying = true;
        try
        {
            // Detach only changed parentage before any insert. In-place property
            // updates and adjacent insertions never clear surviving child controls.
            if (Content is FrameworkElement previousRoot && !ReferenceEquals(previousRoot, nextBindings[root.Id].Element))
                Content = null;
            foreach (var (id, binding) in bindings)
            {
                if (!nextBindings.TryGetValue(id, out var replacement) || !ReferenceEquals(binding, replacement)) Retire(binding);
                if (binding.Children is null) continue;
                for (var index = binding.Children.Children.Count - 1; index >= 0; --index)
                {
                    var child = binding.Children.Children[index];
                    var childId = ((FrameworkElement)child).Tag is WidgetElementIdentity identity ? identity.Id : string.Empty;
                    if (!plan.TryGetValue(childId, out var nextChild) || nextChild.ParentId != id
                        || !nextBindings.TryGetValue(id, out var nextParent) || !ReferenceEquals(nextParent, binding)
                        || !ReferenceEquals(nextBindings[childId].Element, child))
                        binding.Children.Children.RemoveAt(index);
                }
            }
            if (!sameOwner)
            {
                remembered.Clear();
                ResetGroupFocus();
            }
            declarations = plan;
            bindings = nextBindings;
            frame = next;
            foreach (var scope in remembered.Keys.ToArray())
                if (!bindings.TryGetValue(remembered[scope].Id, out var member) || member.Identity != remembered[scope])
                    remembered.Remove(scope);
            foreach (var declaration in plan.Values)
            {
                var binding = bindings[declaration.Node.Id];
                Update(binding, declaration.Node);
                if (binding.Children is { } panel)
                    for (var index = 0; index < declaration.Node.Children.Count; ++index)
                    {
                        var child = bindings[declaration.Node.Children[index].Id].Element;
                        if (index < panel.Children.Count && ReferenceEquals(panel.Children[index], child)) continue;
                        var oldIndex = panel.Children.IndexOf(child);
                        if (oldIndex >= 0) panel.Children.RemoveAt(oldIndex);
                        panel.Children.Insert(index, child);
                    }
            }
            Content = bindings[root.Id].Element;
            needsEntry = needsEntry || !sameOwner || oldScope != next.Authority.ActiveInputScopeId
                || (focused is not null && (!bindings.TryGetValue(focused.Identity.Id, out var current)
                    || !ReferenceEquals(current, focused) || !Eligible(current)));
            if (!presentationOnly) UpdateFocusPolicy(next.Snapshot);
            else needsEntry = false;
            pendingRestore = !needsEntry && focused is not null && Eligible(focused)
                && !ReferenceEquals(FocusedBinding(), focused) ? focused : null;
        }
        finally { applying = false; }
        if (needsEntry || pendingRestore is not null || pendingGroupEntry is not null) QueueEntryFocus();
    }

    private void QueueEntryFocus()
    {
        if (presentationOnly) return;
        if (focusQueued) return;
        focusQueued = true;
        if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, RestoreFocus)) focusQueued = false;
    }

    private void RestoreFocus()
    {
        focusQueued = false;
        if (!IsLoaded || XamlRoot is null || frame is null || applying) return;
        var reassertFocus = restoreNativeFocus;
        restoreNativeFocus = false;
        if (TryRestoreGroupEntry()) return;
        if (reassertFocus && FocusedBinding() is { } current && Eligible(current) &&
            FocusManager.GetFocusedElement(XamlRoot) is Control leaf && leaf.Focus(FocusState.Keyboard))
            needsEntry = false;
        if (pendingRestore is { } restore)
        {
            pendingRestore = null;
            if (bindings.TryGetValue(restore.Identity.Id, out var retained) && ReferenceEquals(retained, restore) && Eligible(restore)
                && !ReferenceEquals(FocusedBinding(), restore)) FocusBinding(restore);
        }
        if (!needsEntry) return;
        var scope = frame.Authority.ActiveInputScopeId;
        Binding? target = null;
        if (remembered.TryGetValue(scope, out var identity) && bindings.TryGetValue(identity.Id, out var rememberedBinding)
            && rememberedBinding.Identity == identity && Eligible(rememberedBinding)) target = rememberedBinding;
        if (target is null && frame.Snapshot.InitialFocusId is { } initial && bindings.TryGetValue(initial, out var initialBinding)
            && Eligible(initialBinding)) target = initialBinding;
        target ??= bindings.Values.FirstOrDefault(Eligible);
        if (target is not null && FocusBinding(target)) needsEntry = false;
    }

    public bool MoveFocus(FocusNavigationDirection direction)
    {
        if (applying || !IsLoaded || frame is null) return false;
        CancelGroupEntry();
        if (FindIndexedCollection()?.MoveFocus(direction) == true) return true;
        var scope = bindings.Values.FirstOrDefault(binding => declarations[binding.Identity.Id].Node.InputScopeId == frame.Authority.ActiveInputScopeId)
            ?? (bindings.GetValueOrDefault(frame.Snapshot.Root.Id)?.Identity.Scope == frame.Authority.ActiveInputScopeId
                ? bindings.GetValueOrDefault(frame.Snapshot.Root.Id) : null);
        return scope is not null && FocusManager.TryMoveFocus(direction, new FindNextElementOptions { SearchRoot = scope.Element });
    }

    public void ActivateFocused()
    {
        if (applying || disposed || presentationOnly) return;
        CancelGroupEntry();
        if (FindIndexedCollection()?.ActivateFocused() == true) return;
        if (!applying && FocusedBinding() is { Element: Button { Command: { } command } button } binding
            && Eligible(binding) && command.CanExecute(button.CommandParameter)) command.Execute(button.CommandParameter);
    }

    private bool Eligible(Binding binding) => frame is not null && binding.Identity.Scope == frame.Authority.ActiveInputScopeId
        && (declarations[binding.Identity.Id].Node.IsFocusable || binding.Element is WidgetIndexedCollectionView)
        && declarations[binding.Identity.Id].Node is { IsDisabled: not true, IsBusy: not true }
        && binding.Element.Visibility == Visibility.Visible;

    private Binding? FocusedBinding() => XamlRoot is null ? null : FindBinding(FocusManager.GetFocusedElement(XamlRoot) as DependencyObject);

    private Binding? FindBinding(DependencyObject? element)
    {
        for (var focused = element; focused is not null && !ReferenceEquals(focused, this);
            focused = VisualTreeHelper.GetParent(focused))
        {
            var id = focused is FrameworkElement { Tag: WidgetElementIdentity identity } ? identity.Id : string.Empty;
            if (bindings.TryGetValue(id, out var binding) && ReferenceEquals(binding.Element, focused)) return binding;
        }
        return null;
    }

    private void RememberFocus()
    {
        if (!applying && FocusedBinding() is { } binding && Eligible(binding))
        {
            remembered[binding.Identity.Scope] = binding.Identity;
            RememberGroupFocus(binding);
            UpdateNativeNeighbors();
        }
    }

    private async Task InvokeAsync(WidgetElementIdentity identity, object token)
    {
        if (applying || frame is null || DispatchActionAsync is null || !bindings.TryGetValue(identity.Id, out var binding)
            || binding.Identity != identity || !ReferenceEquals(binding.Token, token) || !Eligible(binding)
            || declarations[identity.Id].Node.ActionId is not { } action) return;
        CancelGroupEntry();
        var authority = frame.Authority;
        try
        {
            await DispatchActionAsync(new(authority, new(action, identity.Id, Sequence: ++actionSequence,
                MonotonicTimestampMicroseconds: Environment.TickCount64 * 1000, InputScopeId: authority.ActiveInputScopeId)
                { FocusedElementId = FocusedBinding()?.Identity.Id }));
        }
        catch (OperationCanceledException) when (disposed) { }
        catch (Exception error) { ReportFailure(error); }
    }

    private Binding Create(Declaration declaration)
    {
        var node = declaration.Node;
        var token = new object();
        Panel? children = null;
        FrameworkElement element;
        switch (node.Kind)
        {
            case ViewNodeKind.Stack:
            case ViewNodeKind.Row:
                element = children = new Grid();
                break;
            case ViewNodeKind.IndexedCollection:
                if (Session is null) throw new InvalidOperationException("Indexed widgets require a presentation session.");
                element = new WidgetIndexedCollectionView(Session, ReportFailure)
                { FocusRemembered = item => RememberCollectionFocus(declaration.Identity, item) };
                break;
            case ViewNodeKind.Scroll:
                children = new StackPanel { Spacing = 12 };
                element = new ScrollViewer { Content = children, HorizontalContentAlignment = HorizontalAlignment.Stretch };
                break;
            case ViewNodeKind.ActionSurface:
                children = new StackPanel { Spacing = 8 };
                // Worker admission owns action sequencing. A pending IPC response
                // must not suppress another deliberate press on an enabled control.
                element = presentationOnly ? children : new Button { Content = children, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Command = new AsyncRelayCommand(() => InvokeAsync(declaration.Identity, token), AsyncRelayCommandOptions.AllowConcurrentExecutions) };
                break;
            case ViewNodeKind.Button:
                element = presentationOnly ? new TextBlock { TextWrapping = TextWrapping.Wrap } :
                    new Button { Command = new AsyncRelayCommand(() => InvokeAsync(declaration.Identity, token), AsyncRelayCommandOptions.AllowConcurrentExecutions) };
                break;
            case ViewNodeKind.Image: element = new Image(); break;
            case ViewNodeKind.Text: element = new TextBlock { TextWrapping = TextWrapping.Wrap }; break;
            case ViewNodeKind.Progress: element = new ProgressBar(); break;
            case ViewNodeKind.LoadingIndicator: element = new ProgressRing { IsActive = true }; break;
            case ViewNodeKind.Spacer: element = new Border(); break;
            default: throw new NotSupportedException($"WinUI presentation for {node.Kind} is not implemented.");
        }
        element.Tag = declaration.Identity;
        AutomationProperties.SetAutomationId(element, $"Widget.{node.Id}");
        return new(declaration.Identity, element, children, token);
    }

    private void Update(Binding binding, ViewNode node)
    {
        var element = binding.Element;
        if (element is WidgetIndexedCollectionView indexed) indexed.Apply(frame!, node, binding.Identity.Scope);
        if (element is Image image) UpdateImage(binding, image, node);
        if (element is Grid layout) UpdateLayout(layout, node);
        ApplySizeAndTypography(element, node);
        AutomationProperties.SetName(element, node.AccessibilityLabel ?? node.Text ?? node.Id);
        if (element is TextBlock text) text.Text = node.Text ?? string.Empty;
        if (element is Control control)
        {
            control.IsEnabled = node.IsDisabled != true && node.IsBusy != true;
            control.IsTabStop = node.IsFocusable && binding.Identity.Scope == frame!.Authority.ActiveInputScopeId;
            control.IsHitTestVisible = !node.IsFocusable || binding.Identity.Scope == frame!.Authority.ActiveInputScopeId;
        }
        if (element is Button button && node.Kind == ViewNodeKind.Button) button.Content = node.Text;
        if (binding.Children is StackPanel panel && node.Kind == ViewNodeKind.ActionSurface)
            panel.Orientation = node.ActionSurfaceOrientation == ActionSurfaceOrientation.Horizontal ? Orientation.Horizontal : Orientation.Vertical;
        if (element is ScrollViewer scroll)
        {
            var horizontal = node.ScrollAxis == ScrollAxis.Horizontal;
            scroll.HorizontalScrollMode = horizontal ? ScrollMode.Enabled : ScrollMode.Disabled;
            scroll.VerticalScrollMode = horizontal ? ScrollMode.Disabled : ScrollMode.Enabled;
            scroll.HorizontalScrollBarVisibility = horizontal && node.ShowScrollbar != false ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden;
            scroll.VerticalScrollBarVisibility = !horizontal && node.ShowScrollbar != false ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden;
            ((StackPanel)binding.Children!).Orientation = horizontal ? Orientation.Horizontal : Orientation.Vertical;
        }
        if (element is ProgressBar progress)
        {
            progress.Minimum = node.Minimum ?? 0;
            progress.Maximum = node.Maximum ?? 100;
            progress.Value = node.Value ?? 0;
        }
        if (element is ProgressRing ring)
            ring.Width = ring.Height = node.IndicatorSize switch { LoadingIndicatorSize.Compact => 16, LoadingIndicatorSize.Large => 48, _ => 32 };
        if (frame!.RenderStyles.TryGetValue(node.Id, out var style) && style.Base.TryGetValue("font-size", out var font)
            && font.Number is > 0 and <= 512)
        {
            if (element is TextBlock label) label.FontSize = font.Number.Value;
            if (element is Control fontControl) fontControl.FontSize = font.Number.Value;
        }
        else
        {
            if (element is TextBlock label) label.ClearValue(TextBlock.FontSizeProperty);
            if (element is Control fontControl) fontControl.ClearValue(Control.FontSizeProperty);
        }
    }

    private static Dictionary<string, Declaration> Plan(ViewNode root, string rootScope)
    {
        var result = new Dictionary<string, Declaration>(StringComparer.Ordinal);
        Visit(root, rootScope, null, "");
        return result;

        void Visit(ViewNode node, string inheritedScope, string? parent, string itemPath)
        {
            if (node.VisibleWhen is not (null or ResponsiveVisibility.Always) || node.CollectionLayout is not null && node.Kind != ViewNodeKind.IndexedCollection || node.VirtualCollectionWindow is not null
                || node.CollectionAnchorKey is not null || node.CollectionGeneration is not null || node.CollectionResetGeneration is not null
                || node.ScrollNearStartActionId is not null || node.ScrollNearEndActionId is not null
                || node.Kind is not (ViewNodeKind.Stack or ViewNodeKind.Row or ViewNodeKind.Scroll or ViewNodeKind.Button or ViewNodeKind.ActionSurface
                    or ViewNodeKind.Text or ViewNodeKind.Progress or ViewNodeKind.LoadingIndicator or ViewNodeKind.Spacer or ViewNodeKind.IndexedCollection or ViewNodeKind.Image))
                throw new NotSupportedException($"WinUI presentation for {node.Kind} with these declarations is not implemented.");
            var scope = node.InputScopeId ?? inheritedScope;
            var path = node.CollectionItemKey is { } key ? itemPath + key.Length + ":" + key : itemPath;
            result.Add(node.Id, new(node, new(scope, node.Id, node.Kind, path), parent));
            foreach (var child in node.Children) Visit(child, scope, node.Id, path);
        }
    }

    private static bool SameOwner(WidgetPresentationAuthority first, WidgetPresentationAuthority second) =>
        first.WidgetId == second.WidgetId && first.RuntimeGeneration == second.RuntimeGeneration
        && first.PresentationGeneration == second.PresentationGeneration && first.SessionGeneration == second.SessionGeneration
        && first.WidgetInstanceId == second.WidgetInstanceId;
}
