# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

**ScreenTune for Windows** is a native Windows 11 tray app for display color calibration: per-screen tone curves (black / mid / white per RGB channel), warmth (color temperature), gamma, contrast, and saturation, with a master on/off switch and hold-to-compare. It is the Windows port of the macOS app (`../mac`, Swift). Same values, ranges, math, profile JSON and fine-tune steps (`Nudge.cs` = `Nudge` in `../mac/ScreenTune/DisplaySettings.swift`), so profiles move between the two apps and both feel the same. Change both together.

## Build and test

```bash
dotnet build ScreenTune-Windows.sln                      # Debug, win-x64
dotnet test tests/ScreenTune.Core.Tests                  # logic tests (xUnit)
./tools/publish.ps1                                      # self-contained, trimmed: publish/win-x64
./tools/publish.ps1 -Install                             # + %LOCALAPPDATA%\Programs\ScreenTune and a Start menu shortcut
ISCC.exe /DSourceDir=<publish dir> /O<out> installer/ScreenTune.iss  # per-user ScreenTune-Setup.exe (what CI releases; Inno Setup 6)
src/ScreenTune/bin/Debug/net9.0-windows10.0.22621.0/win-x64/ScreenTune.exe [--background | --quit]
python tools/make_icons.py                               # regenerate Assets/*.ico from the macOS icon layers
```

- **Stack**: C# 13, .NET 9, WinUI 3 (Windows App SDK 1.8), CommunityToolkit.WinUI Segmented + SettingsControls. Unpackaged (`WindowsPackageType=None`), Windows App SDK self-contained.
- Only `ScreenTune.Core` is unit tested. Keep it free of Windows / WinUI references so tests run on plain `net9.0`. Anything visual or Win32 has to be checked by running the app.
- `--background` starts without opening the flyout (used by Launch at startup). `--quit` tells the running copy to restore the displays and exit. A second plain launch opens the running copy's flyout.
- Settings: `%LOCALAPPDATA%\ScreenTune\settings.json`. Log (reset each launch: screens, HDR, refused ramps, unhandled exceptions): `%LOCALAPPDATA%\ScreenTune\log.txt`.

## Layout

```
src/ScreenTune.Core/          Pure logic (tested)
  DisplaySettings.cs          The 13 values, ranges, clamping, transfer table, warmth (Tanner Helland blackbody)
  Profiles.cs                 Profile JSON (macOS format) + source-generated JSON context
  SettingsStore.cs            StoredState + atomic JSON file store, clamps on load
  GammaRamp.cs                Settings -> 768-WORD ramp, drift comparison
  ColorMatrix.cs              5x5 saturation matrix (MAGCOLOREFFECT layout)
  Nudge.cs                    Fine steps per value (0.001, warmth 1K), grid snapping
  ScreenLogic.cs              Screen naming, flyout placement, auto-hide taskbar, away/return tracker
src/ScreenTune/
  Program.cs                  Main: single instance mutex, --quit / show forwarding
  App.xaml(.cs)               Tokens and styles; wires model, system window, flyout, settings; tray menu; quit
  CalibrationModel.cs         Screens, per-screen settings, apply pipeline, drift correction, persistence
  Log.cs                      Plain-text diagnostics log
  Interop/Native.cs           All P/Invoke
  Platform/Displays.cs        Screen list: EnumDisplayMonitors + QueryDisplayConfig (names, stable ids, HDR)
  Platform/GammaController.cs SetDeviceGammaRamp per screen, original-ramp restore, drift re-write
  Platform/SaturationEffect.cs MagSetFullscreenColorEffect
  Platform/SystemWindow.cs    Hidden top-level window: tray icon, popup menu, system broadcasts
  Platform/SystemSettings.cs  Launch at startup (HKCU Run), full-range gamma unlock (GdiIcmGammaRange)
  Views/FlyoutWindow.xaml     The tray flyout (header, screens, Tone / Adjust / Preview cards, menu rows)
  Views/SliderRow.xaml        Label / − / slider / + / value row, optional channel tint; buttons, keys, wheel, drag share one step
  Views/SettingsWindow.xaml   Mica settings window: profiles, startup, full range, version
tests/ScreenTune.Core.Tests/  Ported macOS checks + Windows logic
tools/                        make_icons.py, publish.ps1
installer/ScreenTune.iss      Inno Setup script: per-user install, quits a running copy first, removes the startup value on uninstall
```

## How the macOS design maps to Windows

| macOS | Windows |
|---|---|
| Menu bar item + frosted `NSPanel` | Notification-area icon + borderless acrylic `FlyoutWindow` above the taskbar (like Quick Settings) |
| Display UUID (`CGDisplayCreateUUIDFromDisplayID`) | Monitor device path from `DISPLAYCONFIG_TARGET_DEVICE_NAME.monitorDevicePath` |
| `CGSetDisplayTransferByTable` | `SetDeviceGammaRamp` on `CreateDC(\\.\DISPLAYn)` |
| `CGDisplayRestoreColorSyncSettings` | Write back the ramp the screen had before ScreenTune first touched it |
| Private `CABackdropLayer` + `colorSaturate`, per screen | `MagSetFullscreenColorEffect`, whole desktop |
| `NSEvent` monitors for user return | `GetLastInputInfo` polled every 500 ms (`ActivityTracker`) |
| Screen params / wake notifications | `WM_DISPLAYCHANGE`, resume, `GUID_CONSOLE_DISPLAY_STATE`, session unlock |
| `SMAppService.mainApp` | `HKCU\...\Run` value, plus the Startup apps toggle in `StartupApproved\Run` |
| DeskRiv Darwin notifications | Not needed: CursorHop dims Windows with overlay windows, not gamma ramps |

## Architecture

### Per-screen settings

`CalibrationModel` keeps `screen id -> DisplaySettings` (tone only; saturation stored as 1) plus one global saturation. `Current` folds the global saturation into the selected screen's settings and splits it back out on set, so the flyout, reset and profile import/export treat it like the macOS app does. Screens at defaults are removed from the dictionary. Disconnected screens keep their stored settings.

### Apply pipeline

Every `Current` change diffs tone vs saturation, marks the screen tone-dirty and/or saturation-dirty, and one `Flush` runs on the next dispatcher pass (coalesces reset/import into one write; slider ticks still apply live). Saving is debounced 400 ms. Screens whose tone is identity are never written, so Night Light and ICC calibration keep working there.

### Gamma ramps (`GammaController`)

- Before the first write to a screen, its current ramp is saved as the original (it includes any ICC calibration Windows loaded). Compare, off, reset-to-defaults and quit write the original back. If the current ramp already equals the ramp about to be written, a previous run crashed and left it, so linear is used as the original.
- Windows keeps ramps after the process exits, unlike macOS. `App.ShutDown` runs on Quit, `--quit`, `WM_ENDSESSION`, `ProcessExit` and unhandled exceptions. Killing the process skips it.
- GDI refuses ramps far from linear unless `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM\GdiIcmGammaRange = 256`. `SetDeviceGammaRamp` then returns false: the screen is marked blocked, the flyout shows a note linking to Settings, and Settings > Full-range curves sets the value through an elevated `reg.exe`. On the dev machine 4000K passes and 3000K + 0.5 black lift is refused.
- HDR screens: Windows usually ignores ramps; `Displays` reads `DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2` (24H2+, fallback to the older info) and the flyout says so.

### Saturation (`SaturationEffect`)

`MagInitialize` + `MagSetFullscreenColorEffect` with a luma-preserving matrix (row = input channel). One effect for the whole desktop, so saturation is global. If the process dies, Windows drops the effect by itself. Conflicts with Windows color filters / Magnifier, which use the same API.

### Drift correction

Drivers and Windows reset ramps after sleep, lock, UAC, full-screen games and mode changes.
1. A 500 ms timer samples idle time. The first input after 10 s away re-writes drifted ramps immediately (KVM return).
2. Every 2 s, while not away and only if ramps are applied, ramps are read back and re-written if they moved more than 2%.
3. `WM_DISPLAYCHANGE` rebuilds screens (500 ms debounce). Resume, display-on and unlock re-apply everything (500 ms debounce).

### Flyout (`FlyoutWindow`)

- `OverlappedPresenter` with border, no title bar, always on top, hidden from Alt+Tab and taskbar, `DesktopAcrylicBackdrop`, DWM round corners. Width 360 DIP, height = content.
- Placement: `Shell_NotifyIconGetRect` anchor (or cursor), centered over it, 12 DIP from the taskbar, clamped to the work area. An auto-hide taskbar reserves nothing, so `EffectiveWork` subtracts its thickness (`ABM_GETTASKBARPOS`).
- Closes on deactivation, Esc, Alt+F4 (hides), or clicking the icon again (deactivation remembers if the cursor was on the icon so the same click does not reopen it).
- Opening animation: 250 ms slide from the taskbar edge + 120 ms fade (slide skipped when Windows animations are off).
- Right-click on the tray icon shows a native Win32 menu (dark mode via uxtheme ordinals 135/136).

## Gotchas (all hit while building this)

- `AppWindow.ResizeClient` assumes a caption the borderless presenter does not have (adds ~30 DIP). Size the outer frame from `Size - ClientSize` and position by `ClientToScreen` insets instead.
- A `ScrollViewer` stretches its content to the viewport; with the window sized from content height that loops. `Body` is `VerticalAlignment="Top"`.
- `Application.Exit()` from inside a Win32 callback with an activated window trips a WinUI fail-fast (0x80000003). `App.Quit` enqueues the teardown, lets windows really close, then exits.
- Unpackaged `dotnet publish` drops `ScreenTune.pri` (XAML pages then fail with "Cannot locate resource from ms-appx:///Views/..."). `EnableMsixTooling=true` fixes it.
- The self-contained payload is the whole runtime MSIX, including AI/ML/Widgets (onnxruntime + DirectML ~40 MB). `SkipUnusedWindowsAppSdkPayload` in the csproj excludes them via `MicrosoftWindowsAppSDKFilesExcluded`.
- Source-generated System.Text.Json assigns every `init` property, so missing JSON keys became 0. Records use `set` accessors; `DisplaySettings.Default` returns a fresh instance.
- Message-only windows never get broadcasts (`WM_DISPLAYCHANGE`, `TaskbarCreated`), so `SystemWindow` is a hidden top-level popup.
- UI Automation names: buttons whose content is more than one `TextBlock` need `AutomationProperties.Name`.
- `Windows.Storage.Pickers` throw `COMException 0x80004005` before showing anything when the process is elevated (e.g. started by an elevated shell or installer). Profiles use the Windows App SDK `Microsoft.Windows.Storage.Pickers` (`new FileSavePicker(AppWindow.Id)`), which work elevated, and picker errors are caught locally.
- An unhandled exception must end the process after `ShutDown`. Staying alive without a tray icon left an invisible copy holding the single-instance mutex, so every new launch silently forwarded to it ("crashed and doesn't turn on").
- `publish.ps1 -Install` starts the app through `explorer.exe` so it never inherits an elevated token from the shell. The log's first line says `elevated True/False`.

## UI rules

Follow the global `design.md`: no strokes on cards or buttons (Segmented and Settings buttons have their borders removed), no shadows except the window's own, theme resources for everything except the four tokens in `App.xaml` (channel tints use the Windows console palette, warmth uses accent orange), every interactive element has hover / pressed / disabled states. Preview patterns are drawn unprocessed on purpose (the screen's own correction is what you judge them by).
