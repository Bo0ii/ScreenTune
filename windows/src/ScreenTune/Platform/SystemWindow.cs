using System.Runtime.InteropServices;
using Microsoft.Win32;
using ScreenTune.Core;
using static ScreenTune.Interop.Native;

namespace ScreenTune.Platform;

/// <summary>
/// A hidden top-level window that owns the notification-area icon and hears the
/// system broadcasts the app reacts to (display changes, wake, unlock, theme).
/// Message-only windows never receive broadcasts, so this is a real, never-shown popup.
/// </summary>
internal sealed class SystemWindow : IDisposable
{
    public const string ClassName = "ScreenTune.SystemWindow";

    /// <summary>Posted by a second launch of the app to open the flyout in the running one.</summary>
    public const uint ShowFlyoutMessage = WM_APP + 2;

    /// <summary>Posted by "ScreenTune.exe --quit" to quit the running copy cleanly.</summary>
    public const uint QuitMessage = WM_APP + 3;

    private const uint TrayCallbackMessage = WM_APP + 1;
    private const uint TrayIconId = 1;

    public event Action<PixelRect?>? TrayClicked;
    public event Action<int, int>? TrayMenuRequested;
    public event Action? ShowRequested;
    public event Action? QuitRequested;
    public event Action? DisplaysChanged;
    public event Action? Resumed;
    public event Action? SessionEnding;

    private readonly WndProc _wndProc;
    private readonly uint _taskbarCreated;
    private readonly IntPtr _powerNotification;
    private IntPtr _icon;
    private bool _iconAdded;
    private bool _off;
    private string _tooltip = "ScreenTune";
    private bool _disposed;

    public SystemWindow()
    {
        _wndProc = WindowProc; // kept in a field so the GC never collects the callback
        var instance = GetModuleHandleW(null);
        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = instance,
            lpszClassName = ClassName,
        };
        RegisterClassExW(ref wc);
        Handle = CreateWindowExW(WS_EX_TOOLWINDOW, ClassName, "ScreenTune", WS_POPUP,
            0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);

        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        WTSRegisterSessionNotification(Handle, 0);
        var displayState = GUID_CONSOLE_DISPLAY_STATE;
        _powerNotification = RegisterPowerSettingNotification(Handle, ref displayState, 0);
    }

    public IntPtr Handle { get; }

    /// <summary>Adds or updates the notification-area icon. Off draws it dimmed, like a disabled menu bar item.</summary>
    public void SetTrayIcon(bool off, string tooltip)
    {
        _off = off;
        _tooltip = tooltip;
        var icon = LoadTrayIcon();
        var data = IconData(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        data.hIcon = icon;
        if (!_iconAdded)
        {
            _iconAdded = Shell_NotifyIconW(NIM_ADD, ref data);
            if (_iconAdded)
            {
                data.uVersion = NOTIFYICON_VERSION_4;
                Shell_NotifyIconW(NIM_SETVERSION, ref data);
            }
        }
        else
        {
            Shell_NotifyIconW(NIM_MODIFY, ref data);
        }
        if (_icon != IntPtr.Zero) DestroyIcon(_icon);
        _icon = icon;
    }

    /// <summary>Where the icon sits on screen, or null when Windows can't say (it is in the hidden-icons overflow).</summary>
    public PixelRect? TrayIconRect()
    {
        var id = new NOTIFYICONIDENTIFIER
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONIDENTIFIER>(),
            hWnd = Handle,
            uID = TrayIconId,
        };
        return Shell_NotifyIconGetRect(ref id, out var rect) == 0 ? rect.ToPixelRect() : null;
    }

    public bool IsCursorOverTrayIcon()
    {
        if (TrayIconRect() is not { } r || !GetCursorPos(out var p)) return false;
        return p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom;
    }

    /// <summary>Shows a native popup menu at the point and returns the chosen item id, or 0.</summary>
    public uint ShowMenu(int x, int y, IReadOnlyList<(uint Id, string? Text, bool Enabled)> items, uint defaultId)
    {
        var menu = CreatePopupMenu();
        try
        {
            foreach (var (id, text, enabled) in items)
            {
                if (text is null) AppendMenuW(menu, MF_SEPARATOR, UIntPtr.Zero, null);
                else AppendMenuW(menu, MF_STRING | (enabled ? 0 : MF_GRAYED), (UIntPtr)id, text);
            }
            if (defaultId != 0) SetMenuDefaultItem(menu, defaultId, 0);
            // Without this the menu does not close when clicking elsewhere.
            SetForegroundWindow(Handle);
            var chosen = TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY, x, y, Handle, IntPtr.Zero);
            PostMessageW(Handle, WM_NULL, IntPtr.Zero, IntPtr.Zero);
            return (uint)Math.Max(chosen, 0);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_iconAdded)
        {
            var data = IconData(0);
            Shell_NotifyIconW(NIM_DELETE, ref data);
            _iconAdded = false;
        }
        if (_icon != IntPtr.Zero) DestroyIcon(_icon);
        if (_powerNotification != IntPtr.Zero) UnregisterPowerSettingNotification(_powerNotification);
        WTSUnRegisterSessionNotification(Handle);
        DestroyWindow(Handle);
    }

    private NOTIFYICONDATAW IconData(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = Handle,
        uID = TrayIconId,
        uFlags = flags,
        uCallbackMessage = TrayCallbackMessage,
        szTip = _tooltip,
        szInfo = "",
        szInfoTitle = "",
    };

    private IntPtr LoadTrayIcon()
    {
        // The taskbar follows the Windows mode (not the app mode): white glyph on a dark taskbar.
        var lightTaskbar = Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "SystemUsesLightTheme", 0) is int v && v != 0;
        var name = (lightTaskbar ? "TrayLight" : "TrayDark") + (_off ? "Off" : "") + ".ico";
        var dpi = GetDpiForWindow(Handle);
        var size = GetSystemMetricsForDpi(SM_CXSMICON, dpi == 0 ? 96 : dpi);
        return LoadImageW(IntPtr.Zero, Path.Combine(AppContext.BaseDirectory, "Assets", name), IMAGE_ICON, size, size, LR_LOADFROMFILE);
    }

    private IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case TrayCallbackMessage:
                // NOTIFYICON_VERSION_4: event in LOWORD(lParam), anchor point in wParam.
                var evt = (uint)LowWord(lParam);
                if (evt is NIN_SELECT or NIN_KEYSELECT)
                {
                    TrayClicked?.Invoke(TrayIconRect());
                }
                else if (evt == WM_CONTEXTMENU)
                {
                    TrayMenuRequested?.Invoke(LowWord(wParam), HighWord(wParam));
                }
                return IntPtr.Zero;

            case ShowFlyoutMessage:
                ShowRequested?.Invoke();
                return IntPtr.Zero;

            case QuitMessage:
                QuitRequested?.Invoke();
                return IntPtr.Zero;

            case WM_DISPLAYCHANGE:
                DisplaysChanged?.Invoke();
                break;

            case WM_SETTINGCHANGE:
                if (lParam != IntPtr.Zero && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet")
                {
                    SetTrayIcon(_off, _tooltip);
                }
                break;

            case WM_DPICHANGED:
                SetTrayIcon(_off, _tooltip);
                break;

            case WM_POWERBROADCAST:
                var power = (uint)(long)wParam;
                if (power is PBT_APMRESUMEAUTOMATIC or PBT_APMRESUMESUSPEND)
                {
                    Resumed?.Invoke();
                }
                else if (power == PBT_POWERSETTINGCHANGE && lParam != IntPtr.Zero)
                {
                    var setting = Marshal.PtrToStructure<POWERBROADCAST_SETTING>(lParam);
                    if (setting.PowerSetting == GUID_CONSOLE_DISPLAY_STATE && setting.Data == 1) Resumed?.Invoke();
                }
                return 1;

            case WM_WTSSESSION_CHANGE:
                var session = (uint)(long)wParam;
                if (session is WTS_SESSION_UNLOCK or WTS_CONSOLE_CONNECT) Resumed?.Invoke();
                break;

            case WM_ENDSESSION:
                if (wParam != IntPtr.Zero) SessionEnding?.Invoke();
                return IntPtr.Zero;

            default:
                // Explorer restarted: the notification area is new and empty.
                if (msg == _taskbarCreated && _taskbarCreated != 0)
                {
                    _iconAdded = false;
                    SetTrayIcon(_off, _tooltip);
                    return IntPtr.Zero;
                }
                break;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}
