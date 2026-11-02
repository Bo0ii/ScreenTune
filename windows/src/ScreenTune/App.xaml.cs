using System.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using ScreenTune.Core;
using ScreenTune.Interop;
using ScreenTune.Platform;
using ScreenTune.Views;

namespace ScreenTune;

/// <summary>
/// Tray-only app: no main window. Owns the model, the hidden system window (tray icon
/// and system events), the flyout, and the settings window.
/// </summary>
public partial class App : Application
{
    private const uint MenuOpen = 1;
    private const uint MenuToggle = 2;
    private const uint MenuSettings = 3;
    private const uint MenuQuit = 4;

    private readonly bool _startInBackground;
    private CalibrationModel? _model;
    private SystemWindow? _system;
    private FlyoutWindow? _flyout;
    private SettingsWindow? _settings;
    private bool _shutDown;
    private bool _quitting;

    public App(bool startInBackground)
    {
        _startInBackground = startInBackground;
        InitializeComponent();
        // Put the displays back before the process dies; Windows keeps gamma ramps otherwise.
        UnhandledException += (_, e) =>
        {
            Log.Write($"Unhandled: {e.Message} {e.Exception}");
            ShutDown();
            // The tray icon and display handling are gone now. Exit instead of lingering as an
            // invisible process that holds the single-instance lock and swallows new launches.
            Environment.Exit(1);
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => ShutDown();
    }

    public static new App Current => (App)Application.Current;

    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenTune");

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Hidden windows are not "closed", but be explicit: only Quit ends the app.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        Native.AllowDarkMenus();
        Log.Start();

        var model = new CalibrationModel(DispatcherQueue.GetForCurrentThread(),
            new SettingsStore(Path.Combine(DataDirectory, "settings.json")));
        _model = model;
        Log.Write($"ScreenTune {SettingsWindow.Version}, saturation {(model.SaturationSupported ? "supported" : "not supported")}, " +
                  $"full range {(GammaRangeUnlock.IsUnlocked ? "unlocked" : "default")}, elevated {Environment.IsPrivilegedProcess}");

        _system = new SystemWindow();
        _system.TrayClicked += anchor => _flyout?.Toggle(anchor);
        _system.TrayMenuRequested += ShowTrayMenu;
        _system.ShowRequested += () => _flyout?.ShowAt(_system.TrayIconRect());
        _system.DisplaysChanged += model.NotifyDisplaysChanged;
        _system.Resumed += model.NotifyResumed;
        _system.SessionEnding += ShutDown;
        _system.QuitRequested += Quit;

        _flyout = new FlyoutWindow(model, _system);
        model.PropertyChanged += OnModelChanged;
        UpdateTrayIcon();
        LaunchAtStartup.RefreshPath();

        if (!_startInBackground)
        {
            _flyout.ShowAt(_system.TrayIconRect());
        }
    }

    public void ShowSettings()
    {
        if (_model is null) return;
        _flyout?.Dismiss();
        _settings ??= new SettingsWindow(_model);
        _settings.ShowAndActivate();
    }

    public void Quit()
    {
        if (_quitting) return;
        _quitting = true;
        _flyout?.Dismiss();
        // Leave the callback that asked to quit (tray menu, window message, button)
        // before tearing windows down; exiting from inside it trips a WinUI fail-fast.
        DispatcherQueue.GetForCurrentThread().TryEnqueue(() =>
        {
            ShutDown();
            _flyout?.CloseForQuit();
            _settings?.CloseForQuit();
            Exit();
        });
    }

    private void ShutDown()
    {
        if (_shutDown) return;
        _shutDown = true;
        _model?.Dispose();
        _system?.Dispose();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CalibrationModel.IsEnabled)) UpdateTrayIcon();
    }

    private void UpdateTrayIcon()
    {
        var on = _model?.IsEnabled ?? true;
        _system?.SetTrayIcon(off: !on, tooltip: on ? "ScreenTune" : "ScreenTune (off)");
    }

    private void ShowTrayMenu(int x, int y)
    {
        if (_model is null || _system is null) return;
        _flyout?.Dismiss();
        var chosen = _system.ShowMenu(x, y,
        [
            (MenuOpen, "Open ScreenTune", true),
            (MenuToggle, _model.IsEnabled ? "Turn Tuning Off" : "Turn Tuning On", true),
            (0, null, true),
            (MenuSettings, "Settings…", true),
            (MenuQuit, "Quit ScreenTune", true),
        ], defaultId: MenuOpen);

        switch (chosen)
        {
            case MenuOpen: _flyout?.ShowAt(_system.TrayIconRect()); break;
            case MenuToggle: _model.IsEnabled = !_model.IsEnabled; break;
            case MenuSettings: ShowSettings(); break;
            case MenuQuit: Quit(); break;
        }
    }
}
