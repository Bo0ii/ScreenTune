using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using ScreenTune.Core;
using ScreenTune.Interop;
using ScreenTune.Platform;
using Windows.Graphics;
using Microsoft.Windows.Storage.Pickers;
using WinRT.Interop;

namespace ScreenTune.Views;

/// <summary>
/// One reused settings window (Mica, Settings-app style rows): profiles, launch at
/// startup, the full-range unlock, and the version. Closing hides it.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private const double ClientWidth = 560;
    private const double ClientHeight = 600;

    private readonly CalibrationModel _model;
    private readonly IntPtr _hwnd;
    private bool _syncing;
    private bool _placed;
    private bool _closingForQuit;
    private CancellationTokenSource? _messageTimer;

    internal SettingsWindow(CalibrationModel model)
    {
        _model = model;
        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        SystemBackdrop = new MicaBackdrop();
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "ScreenTune.ico");
        AppWindow.SetIcon(iconPath);
        TitleIcon.Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "ScreenTune.png")));

        var presenter = OverlappedPresenter.Create();
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsResizable = false;
        AppWindow.SetPresenter(presenter);
        AppWindow.Closing += (_, e) =>
        {
            if (_closingForQuit) return;
            e.Cancel = true;
            AppWindow.Hide();
        };

        VersionText.Text = Version;
        _model.PropertyChanged += OnModelChanged;
    }

    internal void CloseForQuit()
    {
        _closingForQuit = true;
        _model.PropertyChanged -= OnModelChanged;
        Close();
    }

    internal void ShowAndActivate()
    {
        Refresh();
        if (!_placed)
        {
            _placed = true;
            CenterOnCursorScreen();
        }
        AppWindow.Show(true);
        Activate();
        Native.SetForegroundWindow(_hwnd);
    }

    internal static string Version
    {
        get
        {
            var assembly = Assembly.GetExecutingAssembly();
            var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
            var plus = info.IndexOf('+');
            return plus > 0 ? info[..plus] : info;
        }
    }

    private void CenterOnCursorScreen()
    {
        Native.GetCursorPos(out var cursor);
        if (Native.MonitorAt(new PixelRect(cursor.X, cursor.Y, cursor.X + 1, cursor.Y + 1)) is not { } monitor) return;
        var work = monitor.Info.rcWork.ToPixelRect();
        var width = (int)Math.Round(ClientWidth * monitor.Scale);
        var height = Math.Min((int)Math.Round(ClientHeight * monitor.Scale), work.Height - 48);
        AppWindow.ResizeClient(new SizeInt32(width, height));
        var outer = AppWindow.Size;
        AppWindow.Move(new PointInt32(work.CenterX - outer.Width / 2, work.CenterY - outer.Height / 2));
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CalibrationModel.SelectedScreen) or nameof(CalibrationModel.SelectedScreenId)
            or nameof(CalibrationModel.Screens))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        ScreenName.Text = _model.SelectedScreen?.Name ?? "None";

        var startup = LaunchAtStartup.Get();
        _syncing = true;
        StartupSwitch.IsOn = startup != LaunchAtStartup.State.Off;
        _syncing = false;
        StartupBlocked.Visibility = startup == LaunchAtStartup.State.TurnedOffInSettings ? Visibility.Visible : Visibility.Collapsed;

        var unlocked = GammaRangeUnlock.IsUnlocked;
        UnlockButton.Visibility = unlocked ? Visibility.Collapsed : Visibility.Visible;
        UnlockedText.Visibility = unlocked ? Visibility.Visible : Visibility.Collapsed;
    }

    // Launch at startup

    private void OnStartupToggled(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        try
        {
            LaunchAtStartup.Set(StartupSwitch.IsOn);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            ShowMessage(ProfileMessage, $"Couldn't change Launch at startup: {ex.Message}");
        }
        Refresh();
    }

    private void OnOpenStartupAppsClick(object sender, RoutedEventArgs e) => LaunchAtStartup.OpenStartupAppsSettings();

    // Full-range curves

    private async void OnUnlockClick(object sender, RoutedEventArgs e)
    {
        UnlockButton.IsEnabled = false;
        var result = await GammaRangeUnlock.UnlockAsync();
        UnlockButton.IsEnabled = true;
        Refresh();
        switch (result)
        {
            case true:
                _model.ReapplyAll();
                ShowMessage(UnlockMessage, "Unlocked. If curves are still limited, restart Windows.", persist: true);
                break;
            case false:
                ShowMessage(UnlockMessage, "Couldn't unlock the full range.");
                break;
            case null:
                break; // declined the administrator prompt
        }
    }

    // Profiles

    // Windows App SDK pickers: unlike Windows.Storage.Pickers they also work when the
    // app runs elevated (e.g. started from an admin terminal or an elevated installer).
    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileSavePicker(AppWindow.Id)
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = SafeFileName($"ScreenTune - {_model.SelectedScreen?.Name ?? "Screen"}"),
                DefaultFileExtension = ".json",
            };
            picker.FileTypeChoices.Add("ScreenTune profile", [".json"]);
            var result = await picker.PickSaveFileAsync();
            if (result is null) return;

            await File.WriteAllTextAsync(result.Path, Profiles.Encode(_model.Current));
            ShowMessage(ProfileMessage, $"Exported {Path.GetFileName(result.Path)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException)
        {
            Log.Write($"Export failed: {ex}");
            ShowMessage(ProfileMessage, $"Export failed: {ex.Message}");
        }
    }

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker(AppWindow.Id) { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeFilter.Add(".json");
            var result = await picker.PickSingleFileAsync();
            if (result is null) return;

            _model.ImportProfile(Profiles.Decode(await File.ReadAllTextAsync(result.Path)));
            ShowMessage(ProfileMessage, $"Imported {Path.GetFileName(result.Path)}");
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or COMException)
        {
            Log.Write($"Import failed: {ex}");
            ShowMessage(ProfileMessage, $"Import failed: {(ex is JsonException ? "this isn't a ScreenTune profile." : ex.Message)}");
        }
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '-' : c).ToArray());
    }

    private async void ShowMessage(Microsoft.UI.Xaml.Controls.TextBlock target, string message, bool persist = false)
    {
        target.Text = message;
        target.Visibility = Visibility.Visible;
        if (persist) return;

        _messageTimer?.Cancel();
        var cts = new CancellationTokenSource();
        _messageTimer = cts;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(4), cts.Token);
            target.Visibility = Visibility.Collapsed;
        }
        catch (TaskCanceledException)
        {
        }
    }
}
