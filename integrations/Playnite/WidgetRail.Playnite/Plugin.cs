using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;

namespace WidgetRail.Playnite;

public sealed class WidgetRailPlaynitePlugin : GenericPlugin
{
    private static readonly ILogger Log = LogManager.GetLogger();
    private readonly IntegrationSettings settings;
    private LocalApiServer? server;
    private PlayniteApiDispatcher? dispatcher;
    private ConnectionDescriptor? descriptor;
    private FileStream? profileLease;
    private string status = "Not started";
    private bool started;
    public override Guid Id => Guid.Parse(IntegrationIdentity.PluginId);

    public WidgetRailPlaynitePlugin(IPlayniteAPI api) : base(api)
    {
        settings = LoadPluginSettings<IntegrationSettings>() ?? new IntegrationSettings();
        settings.Save = () => { SavePluginSettings(settings); if (started) { StopServer(); StartServer(); } };
        Properties = new GenericPluginProperties { HasSettings = true };
    }
    public override void OnApplicationStarted(OnApplicationStartedEventArgs args) { started = true; StartServer(); }
    public override void OnApplicationStopped(OnApplicationStoppedEventArgs args) { started = false; StopServer(); }
    public override void OnGameStarting(OnGameStartingEventArgs args) => dispatcher?.CaptureGameStarting(args.Game.Id, args.Game.Name);
    public override void OnGameStartupCancelled(OnGameStartupCancelledEventArgs args) => dispatcher?.GameStartCancelled(args.Game.Id);
    public override void OnGameStarted(OnGameStartedEventArgs args)
    {
        dispatcher?.CaptureGameStarted(args.Game.Id, args.StartedProcessId, args.Game.InstallDirectory);
        dispatcher?.GameStateChanged(args.Game.Id);
    }
    public override void OnGameStopped(OnGameStoppedEventArgs args)
    {
        dispatcher?.CaptureGameStopped(args.Game.Id);
        dispatcher?.GameStateChanged(args.Game.Id);
    }
    public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args) => dispatcher?.LibraryChanged();

    private void StartServer()
    {
        if (!settings.Enabled) { status = "Disabled"; return; }
        var root = GetPluginUserDataPath();
        try
        {
            Directory.CreateDirectory(root);
            profileLease = new FileStream(Path.Combine(root, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var authPath = Path.Combine(root, IntegrationIdentity.CredentialFile);
            var auth = IntegrationFiles.Read<IntegrationCredentials>(authPath);
            if (auth == null) { auth = IntegrationCredentials.Create(); IntegrationFiles.Write(authPath, auth); }
            if (!auth.IsValid()) throw new InvalidDataException("Connection credentials are invalid. Reconnect through WidgetRail setup.");
            var previous = IntegrationFiles.Read<ConnectionDescriptor>(Path.Combine(root, IntegrationIdentity.DescriptorFile));
            descriptor = ConnectionDescriptor.Create(auth.ProfileId, IntegrationIdentity.FirstPort);
            dispatcher = new PlayniteApiDispatcher(PlayniteApi, descriptor);
            server = new LocalApiServer(auth.Token, descriptor.InstanceId, dispatcher)
                { Diagnostic = code => Log.Warn("WidgetRail local API: " + code) };
            descriptor.Port = server.Start(IntegrationIdentity.PortCandidates(settings.PreferredPort == 0 ? (int?)null : settings.PreferredPort,
                settings.LastSuccessfulPort >= 1024 && settings.LastSuccessfulPort <= 65535 && settings.LastSuccessfulPort != 19821
                    ? (int?)settings.LastSuccessfulPort : previous != null && previous.IsValid() ? (int?)previous.Port : null));
            settings.LastSuccessfulPort = descriptor.Port;
            SavePluginSettings(settings);
            IntegrationFiles.Write(Path.Combine(root, IntegrationIdentity.DescriptorFile), descriptor);
            status = "Connected locally on port " + descriptor.Port;
            Log.Info("WidgetRail local API ready on loopback port " + descriptor.Port);
        }
        catch (Exception error)
        {
            // Do not log auth material, provider payloads or user-library data.
            status = error is System.Net.HttpListenerException ? "The local listener could not start. Check port access in settings."
                : error is IOException ? "The local integration could not start. Another instance or service may be using its files or ports."
                : "The local integration could not start. Reconnect through WidgetRail setup.";
            Log.Warn("WidgetRail local API startup failed: " + error.GetType().Name);
            StopServer();
        }
    }
    private void StopServer()
    {
        dispatcher?.Dispose();
        server?.Dispose(); server = null; dispatcher = null;
        if (descriptor != null && profileLease != null)
        {
            try { IntegrationFiles.ClearOwnedDescriptor(Path.Combine(GetPluginUserDataPath(), IntegrationIdentity.DescriptorFile), descriptor.InstanceId); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is System.Runtime.Serialization.SerializationException)
            { Log.Warn("WidgetRail local descriptor cleanup failed."); }
        }
        descriptor = null; profileLease?.Dispose(); profileLease = null;
    }
    public override ISettings GetSettings(bool firstRunSettings) => settings;
    public override UserControl GetSettingsView(bool firstRunSettings)
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "WidgetRail for Playnite", FontSize = 20, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new TextBlock { Text = status, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        var enabled = new CheckBox { Content = "Enable local WidgetRail integration", Margin = new Thickness(0, 0, 0, 12) };
        enabled.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(IntegrationSettings.Enabled)) { Source = settings, Mode = BindingMode.TwoWay });
        panel.Children.Add(enabled);
        panel.Children.Add(new TextBlock { Text = "Preferred port (0 selects automatically)", Margin = new Thickness(0, 0, 0, 4) });
        var port = new TextBox { MaxLength = 5, Width = 140, HorizontalAlignment = HorizontalAlignment.Left };
        port.SetBinding(TextBox.TextProperty, new Binding(nameof(IntegrationSettings.PreferredPort)) { Source = settings, Mode = BindingMode.TwoWay });
        panel.Children.Add(port);
        panel.Children.Add(new TextBlock { Text = "Local connections only. Connect or repair this profile from WidgetRail's Playnite widget.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
        return new UserControl { Content = panel };
    }
    public override void Dispose() { started = false; StopServer(); base.Dispose(); }
}

public sealed class IntegrationSettings : ISettings
{
    public bool Enabled { get; set; } = true;
    public int PreferredPort { get; set; }
    public int LastSuccessfulPort { get; set; }
    internal Action? Save;
    private bool savedEnabled;
    private int savedPort;
    public void BeginEdit() { savedEnabled = Enabled; savedPort = PreferredPort; }
    public void CancelEdit() { Enabled = savedEnabled; PreferredPort = savedPort; }
    public void EndEdit() => Save?.Invoke();
    public bool VerifySettings(out List<string> errors)
    {
        errors = new List<string>();
        if (PreferredPort != 0 && (PreferredPort < 1024 || PreferredPort > 65535 || PreferredPort == 19821))
            errors.Add("Use 0 for automatic selection, or a port from 1024 to 65535 other than Bridge's 19821.");
        return errors.Count == 0;
    }
}
