using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class LocalWidgetInstallDialog : ContentDialog
{
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap, MaxWidth = 560 };
    private readonly ProgressRing progress = new() { IsActive = true, Width = 28, Height = 28, HorizontalAlignment = HorizontalAlignment.Left };
    private TaskCompletionSource<bool>? approval;
    internal bool Finished { get; private set; }
    internal event Action? StateChanged;
    internal bool ControllerArmed { get; set; }

    internal LocalWidgetInstallDialog()
    {
        Input.GamepadKeyBoundary.ObserveDialog(this);
        AutomationProperties.SetAutomationId(this, "Host.WidgetInstall.Dialog");
        AutomationProperties.SetAutomationId(message, "Host.WidgetInstall.Status");
        AutomationProperties.SetLiveSetting(message, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        Title = "Install widget";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Close;
        Content = new StackPanel { Spacing = 16, Children = { progress, message } };
        message.Text = "Checking the widget package…";
        Opened += (_, _) => FocusCancel();
        PrimaryButtonClick += (_, args) => { args.Cancel = true; Approve(); };
        Closing += (_, _) => approval?.TrySetResult(false);
    }

    internal async Task<bool> ReviewAsync(LocalWidgetInstallResult package, CancellationToken cancellationToken)
    {
        approval = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Title = "Review full-access widget";
        message.Text = $"{package.WidgetId}  {package.Version}\n\nThis widget can access your files, network, and other apps with your Windows account’s permissions. It is not sandboxed, and its publisher is not verified.\n\nOnly continue if you trust where you downloaded it. It will be installed disabled; enable it separately after reviewing permissions.";
        progress.IsActive = false; progress.Visibility = Visibility.Collapsed;
        PrimaryButtonText = "Install disabled";
        IsPrimaryButtonEnabled = true;
        ControllerArmed = false;
        FocusCancel(); StateChanged?.Invoke();
        try { return await approval.Task.WaitAsync(cancellationToken); }
        finally { approval = null; }
    }

    private void Approve()
    {
        if (approval is not { } decision || decision.Task.IsCompleted) return;
        decision.TrySetResult(true);
        PrimaryButtonText = "";
        message.Text = "Installing the widget disabled…";
        progress.Visibility = Visibility.Visible; progress.IsActive = true;
        ControllerArmed = false; FocusCancel(); StateChanged?.Invoke();
    }

    internal void Complete(LocalWidgetInstallResult result)
    {
        Finished = true; approval?.TrySetResult(false);
        Title = result.Status == "installed-disabled" ? "Widget installed" : result.Status == "cancelled" ? "Installation cancelled" : "Could not install widget";
        message.Text = result.Message;
        PrimaryButtonText = ""; CloseButtonText = "Done";
        progress.IsActive = false; progress.Visibility = Visibility.Collapsed;
        ControllerArmed = false; FocusCancel(); StateChanged?.Invoke();
    }

    internal void FocusCancel() => (GetTemplateChild("CloseButton") as Control)?.Focus(FocusState.Keyboard);
    internal void MoveFocus(FocusNavigationDirection direction) =>
        FocusManager.TryMoveFocus(direction, new FindNextElementOptions { SearchRoot = this });
    internal void Handle(ControllerButton button, ControllerEventPhase phase)
    {
        if (!ControllerArmed || phase != ControllerEventPhase.Pressed) return;
        if (button == ControllerButton.B) { ControllerArmed = false; Hide(); }
        else if (button == ControllerButton.A && FocusManager.GetFocusedElement(XamlRoot) is Button focused &&
            (ReferenceEquals(focused, GetTemplateChild("PrimaryButton")) || ReferenceEquals(focused, GetTemplateChild("CloseButton"))))
        {
            ControllerArmed = false;
            (FrameworkElementAutomationPeer.CreatePeerForElement(focused)?.GetPattern(PatternInterface.Invoke) as IInvokeProvider)?.Invoke();
        }
    }
}
