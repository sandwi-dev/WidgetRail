using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed record HostChoiceSelection(string Id, string Label);

internal sealed partial class HostChoiceDialog
{
    private sealed record SelectionPopup(WidgetValueButton Button, MenuFlyout Menu, ToggleMenuFlyoutItem[] Items, IDisposable? Theme, Action<int> Select)
    { internal bool RestoreFocus; }
    private SelectionPopup? selectionPopup;
    private int selectionFocus;
    private readonly HashSet<string> selectionIds = new(StringComparer.Ordinal);
    internal string ControllerBackLabel => selectionPopup is null ? CloseButtonText : "Back";

    /// <summary>A bounded native value selector inside a host choice dialog; selecting a value never confirms the main action.</summary>
    internal Func<string> AddSelectionOption(string label, string automationId, IReadOnlyList<HostChoiceSelection> values, string selectedId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(automationId);
        var options = values.ToArray();
        if (selectionIds.Count >= 4 || booleanOptions.ContainsKey(automationId) || options.Length is < 1 or > 16 ||
            options.Any(value => string.IsNullOrWhiteSpace(value.Id) || string.IsNullOrWhiteSpace(value.Label)) ||
            options.Select(value => value.Id).Distinct(StringComparer.Ordinal).Count() != options.Length ||
            !options.Any(value => value.Id == selectedId) || !selectionIds.Add(automationId))
            throw new ArgumentException("A host selector requires a unique identity, bounded distinct values and a valid selection.");
        var selected = selectedId;
        var button = new WidgetValueButton { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(button, automationId);
        AutomationProperties.SetName(button, label);
        button.ExpandRequested = Open;
        button.CollapseRequested = () => { if (selectionPopup?.Button == button) CloseSelection(); };
        button.Click += (_, _) => Open();
        button.GotFocus += (_, _) => SelectionContextChanged?.Invoke();
        details.Children.Add(button);
        Update();
        return () => selected;

        void Update()
        {
            var value = options.Single(option => option.Id == selected).Label;
            button.SetLabel(label + ": " + value);
            button.SetAccessibleValue(value, isSensitive: false);
        }
        void Open()
        {
            if (!opened || closing || !button.IsLoaded || selectionPopup is not null) return;
            initialFocusRequired = false;
            var menu = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };
            Input.GamepadKeyBoundary.ObserveFlyout(menu, button);
            var items = options.Select(value => new ToggleMenuFlyoutItem { Text = value.Label, IsChecked = value.Id == selected }).ToArray();
            foreach (var item in items) menu.Items.Add(item);
            void Select(int index)
            {
                if (selectionPopup?.Menu != menu || closing || index < 0 || index >= options.Length) return;
                selected = options[index].Id; Update(); CloseSelection(restoreFocus: true);
            }
            var popup = new SelectionPopup(button, menu, items, NativePopupTheme.Menu(menu, this), Select);
            selectionPopup = popup;
            button.SetExpanded(true);
            for (var index = 0; index < items.Length; index++)
            {
                var option = options[index];
                var itemIndex = index;
                AutomationProperties.SetAutomationId(items[index], automationId + "." + option.Id);
                items[index].GotFocus += (_, _) => selectionFocus = itemIndex;
                items[index].Click += (_, _) => Select(itemIndex);
            }
            menu.Opened += (_, _) =>
            {
                if (!ReferenceEquals(selectionPopup, popup)) return;
                selectionFocus = Array.FindIndex(options, option => option.Id == selected);
                items[selectionFocus].Focus(FocusState.Keyboard);
            };
            menu.Closed += (_, _) =>
            {
                popup.Theme?.Dispose();
                if (ReferenceEquals(selectionPopup, popup)) { selectionPopup = null; button.SetExpanded(false); }
                if (popup.RestoreFocus && opened && !closing && selectionPopup is null) button.Focus(FocusState.Keyboard);
                SelectionContextChanged?.Invoke();
            };
            try { menu.ShowAt(button); SelectionContextChanged?.Invoke(); }
            catch { CloseSelection(); throw; }
        }
    }

    private void CloseSelection(bool restoreFocus = false)
    {
        if (selectionPopup is not { } popup) return;
        selectionPopup = null;
        popup.RestoreFocus = restoreFocus;
        popup.Button.SetExpanded(false);
        popup.Menu.Hide();
        popup.Theme?.Dispose();
        SelectionContextChanged?.Invoke();
    }
    private bool HandleSelection(ControllerButton button)
    {
        if (selectionPopup is not { } popup) return false;
        if (button == ControllerButton.B) CloseSelection(restoreFocus: true);
        else if (button == ControllerButton.A && FocusManager.GetFocusedElement(XamlRoot) is ToggleMenuFlyoutItem focused)
            popup.Select(Array.IndexOf(popup.Items, focused));
        return true;
    }
}
