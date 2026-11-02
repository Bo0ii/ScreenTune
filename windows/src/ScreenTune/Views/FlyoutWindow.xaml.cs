using System.ComponentModel;
using System.Numerics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using ScreenTune.Core;
using ScreenTune.Interop;
using ScreenTune.Platform;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;

namespace ScreenTune.Views;

/// <summary>
/// The tray flyout: a borderless, acrylic, always-on-top window placed above the
/// notification-area icon like the Windows 11 Quick Settings and volume flyouts.
/// Closes on outside click (deactivation), Esc, or clicking the icon again.
/// </summary>
public sealed partial class FlyoutWindow : Window
{
    private const double ContentWidth = 360;
    private const int EdgeMargin = 12;

    private readonly CalibrationModel _model;
    private readonly SystemWindow _system;
    private readonly IntPtr _hwnd;
    private readonly bool _animationsEnabled = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;

    private int _point = 1; // 0 black, 1 middle, 2 white
    private bool _syncing;
    private bool _isOpen;
    private bool _closingForQuit;
    private (PixelRect Anchor, PixelRect Work, TaskbarEdge Edge, double Scale)? _placement;

    // Clicking the icon while open first deactivates (closing the flyout), then reports
    // the click. Remember that so the click does not reopen it straight away.
    private bool _suppressNextTrayClick;
    private DateTime _hiddenAt = DateTime.MinValue;

    internal FlyoutWindow(CalibrationModel model, SystemWindow system)
    {
        _model = model;
        _system = system;
        InitializeComponent();

        _hwnd = WindowNative.GetWindowHandle(this);
        ConfigureWindow();
        WireControls();

        Activated += OnActivated;
        Body.SizeChanged += (_, _) => { if (_isOpen) Place(Body.ActualHeight); };
        _model.PropertyChanged += OnModelChanged;
        RefreshAll();
    }

    // Showing and hiding

    internal void Toggle(PixelRect? anchor)
    {
        if (_isOpen)
        {
            Dismiss();
            return;
        }
        if (_suppressNextTrayClick && DateTime.UtcNow - _hiddenAt < TimeSpan.FromSeconds(1))
        {
            _suppressNextTrayClick = false;
            return;
        }
        ShowAt(anchor);
    }

    internal void ShowAt(PixelRect? anchor)
    {
        _suppressNextTrayClick = false;
        var a = anchor ?? CursorRect();
        if (Native.MonitorAt(a) is not { } monitor) return;
        var monitorRect = monitor.Info.rcMonitor.ToPixelRect();
        var edge = FlyoutPlacement.DetectEdge(monitorRect, monitor.Info.rcWork.ToPixelRect(), a);
        var work = FlyoutPlacement.EffectiveWork(monitorRect, monitor.Info.rcWork.ToPixelRect(), edge,
            TaskbarThickness(edge, monitor.Scale));
        _placement = (a, work, edge, monitor.Scale);

        RefreshAll();
        Body.Measure(new Size(ContentWidth, double.PositiveInfinity));
        Place(Body.DesiredSize.Height > 100 ? Body.DesiredSize.Height : 640);

        _isOpen = true;
        AppWindow.Show(true);
        Activate();
        Native.SetForegroundWindow(_hwnd);
        CompareButton.Focus(FocusState.Programmatic);
        AnimateIn(edge);
    }

    private static int TaskbarThickness(TaskbarEdge edge, double scale)
    {
        if (Native.TaskbarRect() is not { } bar) return (int)Math.Round(48 * scale);
        return edge is TaskbarEdge.Left or TaskbarEdge.Right ? bar.Width : bar.Height;
    }

    internal void CloseForQuit()
    {
        _closingForQuit = true;
        _model.PropertyChanged -= OnModelChanged;
        Close();
    }

    internal void Dismiss()
    {
        if (!_isOpen) return;
        _isOpen = false;
        _model.IsComparing = false;
        AppWindow.Hide();
        _hiddenAt = DateTime.UtcNow;
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated || !_isOpen) return;
        _suppressNextTrayClick = _system.IsCursorOverTrayIcon();
        Dismiss();
    }

    private void ConfigureWindow()
    {
        AppWindow.IsShownInSwitchers = false;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "ScreenTune.ico"));
        AppWindow.Closing += (_, e) =>
        {
            if (_closingForQuit) return;
            // Alt+F4 hides it like Esc; the app keeps running in the tray.
            e.Cancel = true;
            Dismiss();
        };

        var presenter = OverlappedPresenter.Create();
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsResizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);

        SystemBackdrop = new DesktopAcrylicBackdrop();
        var round = Native.DWMWCP_ROUND;
        Native.DwmSetWindowAttribute(_hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
    }

    private void Place(double contentHeight)
    {
        if (_placement is not { } p) return;
        var margin = (int)Math.Round(EdgeMargin * p.Scale);
        var width = (int)Math.Round(ContentWidth * p.Scale);
        var height = Math.Min((int)Math.Ceiling(contentHeight * p.Scale), FlyoutPlacement.MaxHeight(p.Work, margin));

        // ResizeClient assumes a caption this window does not have, so size the outer
        // frame from the measured frame thickness, and place by the client area so the
        // invisible resize borders do not count toward the margins.
        var frameWidth = AppWindow.Size.Width - AppWindow.ClientSize.Width;
        var frameHeight = AppWindow.Size.Height - AppWindow.ClientSize.Height;
        AppWindow.Resize(new SizeInt32(width + frameWidth, height + frameHeight));
        var (insetLeft, insetTop) = Native.ClientInsets(_hwnd);
        var rect = FlyoutPlacement.Place(p.Anchor, p.Work, p.Edge, width, height, margin);
        AppWindow.Move(new PointInt32(rect.Left - insetLeft, rect.Top - insetTop));
    }

    private void AnimateIn(TaskbarEdge edge)
    {
        var visual = ElementCompositionPreview.GetElementVisual(Root);
        ElementCompositionPreview.SetIsTranslationEnabled(Root, true);
        var compositor = visual.Compositor;

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 0f);
        fade.InsertKeyFrame(1f, 1f);
        fade.Duration = TimeSpan.FromMilliseconds(120);
        visual.StartAnimation("Opacity", fade);

        if (!_animationsEnabled) return;
        const float distance = 24f;
        var from = edge switch
        {
            TaskbarEdge.Top => new Vector3(0, -distance, 0),
            TaskbarEdge.Left => new Vector3(-distance, 0, 0),
            TaskbarEdge.Right => new Vector3(distance, 0, 0),
            _ => new Vector3(0, distance, 0),
        };
        var slide = compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(0f, from);
        slide.InsertKeyFrame(1f, Vector3.Zero,
            compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f)));
        slide.Duration = TimeSpan.FromMilliseconds(250);
        visual.StartAnimation("Translation", slide);
    }

    private static PixelRect CursorRect()
    {
        Native.GetCursorPos(out var p);
        return new PixelRect(p.X, p.Y, p.X + 1, p.Y + 1);
    }

    // Controls

    private void WireControls()
    {
        TonePoint.SelectionChanged += OnTonePointChanged;
        RedRow.ValueChanged += v => _model.SetValue(ToneKey(0), v);
        GreenRow.ValueChanged += v => _model.SetValue(ToneKey(1), v);
        BlueRow.ValueChanged += v => _model.SetValue(ToneKey(2), v);

        // The warmth slider runs left to right from neutral to warm, so it is the Kelvin value mirrored.
        var kelvin = DisplaySettings.TemperatureRange;
        WarmthRow.SetRange(kelvin.Min, kelvin.Max, Nudge.Step(SettingKey.Temperature));
        WarmthRow.Format = v => $"{Math.Round(Mirror(v), MidpointRounding.AwayFromZero):0}K";
        WarmthRow.ValueChanged += v => _model.SetValue(SettingKey.Temperature, Mirror(v));

        foreach (var (row, key) in new[]
        {
            (GammaRow, SettingKey.Gamma),
            (ContrastRow, SettingKey.Contrast),
            (SaturationRow, SettingKey.Saturation),
        })
        {
            var range = DisplaySettings.RangeOf(key);
            row.SetRange(range.Min, range.Max, Nudge.Step(key));
            row.ValueChanged += v => _model.SetValue(key, v);
        }

        // Hold to compare: original colors only while the button is held down.
        CompareButton.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => SetComparing(true)), true);
        CompareButton.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => SetComparing(false)), true);
        CompareButton.PointerCaptureLost += (_, _) => SetComparing(false);
        CompareButton.PointerCanceled += (_, _) => SetComparing(false);
        CompareButton.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, e) =>
        {
            if (e.Key is VirtualKey.Space or VirtualKey.Enter) SetComparing(true);
        }), true);
        CompareButton.AddHandler(UIElement.KeyUpEvent, new KeyEventHandler((_, e) =>
        {
            if (e.Key is VirtualKey.Space or VirtualKey.Enter) SetComparing(false);
        }), true);
        CompareButton.LostFocus += (_, _) => SetComparing(false);

        Root.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        AddAccelerator(VirtualKey.Escape, VirtualKeyModifiers.None, Dismiss);
        AddAccelerator((VirtualKey)0xBC, VirtualKeyModifiers.Control, () => App.Current.ShowSettings()); // Ctrl+,
        AddAccelerator(VirtualKey.Q, VirtualKeyModifiers.Control, () => App.Current.Quit());
    }

    private void AddAccelerator(VirtualKey key, VirtualKeyModifiers modifiers, Action action)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        accelerator.Invoked += (_, e) =>
        {
            e.Handled = true;
            action();
        };
        Root.KeyboardAccelerators.Add(accelerator);
    }

    private void SetComparing(bool on) => _model.IsComparing = on && _model.IsEnabled;

    private SettingKey ToneKey(int channel) => (SettingKey)(_point * 3 + channel);

    private static double Mirror(double value) =>
        DisplaySettings.TemperatureRange.Min + DisplaySettings.TemperatureRange.Max - value;

    private void OnEnabledToggled(object sender, RoutedEventArgs e)
    {
        if (!_syncing) _model.IsEnabled = EnabledSwitch.IsOn;
    }

    private void OnTonePointChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TonePoint.SelectedIndex < 0) return;
        _point = TonePoint.SelectedIndex;
        RefreshValues();
    }

    private void OnResetClick(object sender, RoutedEventArgs e) => _model.ResetCurrent();

    private void OnSettingsClick(object sender, RoutedEventArgs e) => App.Current.ShowSettings();

    private void OnQuitClick(object sender, RoutedEventArgs e) => App.Current.Quit();

    // Refreshing from the model

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(CalibrationModel.Current):
                RefreshValues();
                break;
            case nameof(CalibrationModel.IsEnabled):
                RefreshEnabled();
                break;
            case nameof(CalibrationModel.SelectedScreenIsBlocked):
                RefreshNote();
                break;
            case nameof(CalibrationModel.Screens):
            case nameof(CalibrationModel.SelectedScreen):
            case nameof(CalibrationModel.SelectedScreenId):
                RefreshAll();
                break;
        }
    }

    private void RefreshAll()
    {
        RefreshEnabled();
        RefreshScreens();
        RefreshValues();
        RefreshNote();
    }

    private void RefreshEnabled()
    {
        _syncing = true;
        EnabledSwitch.IsOn = _model.IsEnabled;
        _syncing = false;
        CompareButton.IsEnabled = _model.IsEnabled;
        CardsHost.IsEnabled = _model.IsEnabled;
        CardsHost.Opacity = _model.IsEnabled ? 1 : 0.45;
    }

    private void RefreshValues()
    {
        var current = _model.Current;
        var toneRange = DisplaySettings.RangeOf(ToneKey(0));
        foreach (var (row, channel) in new[] { (RedRow, 0), (GreenRow, 1), (BlueRow, 2) })
        {
            row.SetRange(toneRange.Min, toneRange.Max, Nudge.Step(ToneKey(channel)));
            row.SetValue(current.Get(ToneKey(channel)));
        }
        WarmthRow.SetValue(Mirror(current.Temperature));
        GammaRow.SetValue(current.Gamma);
        ContrastRow.SetValue(current.Contrast);
        SaturationRow.SetValue(current.Saturation);

        SaturationRow.IsEnabled = _model.SaturationSupported;
        SaturationNote.Text = !_model.SaturationSupported ? "Saturation isn't available on this PC."
            : _model.Screens.Count > 1 ? "Saturation applies to every screen."
            : "";
        SaturationNote.Visibility = SaturationNote.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        ResetButton.IsEnabled = current != DisplaySettings.Default;
        ResetText.Text = _model.Screens.Count > 1 && _model.SelectedScreen is { } s ? $"Reset {s.Name}" : "Reset to Defaults";
    }

    private void RefreshScreens()
    {
        var screens = _model.Screens;
        ScreenSection.Visibility = screens.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        ScreenList.Children.Clear();
        if (screens.Count < 2) return;

        var secondary = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        foreach (var screen in screens)
        {
            var selected = screen.Id == _model.SelectedScreenId;
            var grid = new Grid { ColumnSpacing = 12 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new FontIcon { Glyph = screen.IsBuiltIn ? "" : "", FontSize = 16, Foreground = secondary };
            var name = new TextBlock { Text = screen.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            var check = new FontIcon { Glyph = "", FontSize = 12, Visibility = selected ? Visibility.Visible : Visibility.Collapsed };
            Grid.SetColumn(name, 1);
            Grid.SetColumn(check, 2);
            grid.Children.Add(icon);
            grid.Children.Add(name);
            grid.Children.Add(check);

            var row = new Button { Style = (Style)Application.Current.Resources["MenuRowButtonStyle"], Content = grid };
            AutomationProperties.SetName(row, selected ? $"{screen.Name}, selected" : screen.Name);
            var id = screen.Id;
            row.Click += (_, _) => _model.SelectedScreenId = id;
            ScreenList.Children.Add(row);
        }
    }

    private void RefreshNote()
    {
        NoteText.Inlines.Clear();
        var screen = _model.SelectedScreen;
        if (screen?.IsHdr == true)
        {
            NoteGlyph.Glyph = ""; // Info
            NoteGlyph.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
            NoteText.Inlines.Add(new Run { Text = "HDR is on for this screen. Windows usually ignores tone curves while HDR is on." });
        }
        else if (_model.SelectedScreenIsBlocked)
        {
            NoteGlyph.Glyph = ""; // Warning
            NoteGlyph.Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
            NoteText.Inlines.Add(new Run { Text = "Windows limited this curve. " });
            var link = new Hyperlink();
            link.Inlines.Add(new Run { Text = "Allow the full range" });
            link.Click += (_, _) => App.Current.ShowSettings();
            NoteText.Inlines.Add(link);
        }
        NotePanel.Visibility = NoteText.Inlines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
