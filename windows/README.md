# ScreenTune for Windows

Per-screen display tuning from the notification area: tone curves (black, middle and white points per color channel), warmth, gamma, contrast and saturation, with an on/off switch and hold-to-compare. The Windows version of ScreenTune for Mac, with the same controls and math. Profile files work in both.

## Install

Requires Windows 10 (19041) or Windows 11, x64 or ARM64.

```powershell
./tools/publish.ps1 -Install
```

This builds a self-contained copy (no .NET or Windows App SDK install needed), puts it in `%LOCALAPPDATA%\Programs\ScreenTune`, adds a Start menu shortcut and starts it. Without `-Install` the build lands in `publish\win-x64`.

New notification-area icons start in the hidden-icons overflow. To keep ScreenTune on the taskbar: Settings > Personalization > Taskbar > Other system tray icons > ScreenTune.

## Use

- **Click** the tray icon for the flyout. Pick a screen (with two or more), then adjust Tone and Adjust. Changes apply live.
- **Hold** the eye button to see the original colors. The switch turns all tuning off without losing it.
- **Right-click** the icon for a quick menu (open, turn off, settings, quit).
- **Settings** (Ctrl+, in the flyout): import and export profiles, launch at startup, and Full-range curves.

## Good to know

- **Saturation applies to every screen.** Windows only offers a desktop-wide color matrix for it.
- **Full-range curves.** Windows refuses tone curves that move far from normal (very warm, big black lift, very low contrast). The flyout tells you when that happens. Settings > Full-range curves lifts the limit for the PC after an administrator prompt.
- **HDR.** Windows usually ignores tone curves on a screen in HDR mode. The flyout says when the selected screen has HDR on.
- **Night Light / Color filters.** Screens left at defaults are never touched, so Night Light keeps working there. Saturation shares its mechanism with Windows color filters; use one or the other.
- Quit ScreenTune (tray menu or `ScreenTune.exe --quit`) to put every screen back exactly as it was.

## Build

```bash
dotnet build ScreenTune-Windows.sln
dotnet test tests/ScreenTune.Core.Tests
```

.NET 9 SDK, WinUI 3 (Windows App SDK 1.8).
