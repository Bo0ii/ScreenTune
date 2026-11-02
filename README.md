<div align="center">

# ScreenTune

**Per-screen color tuning for Mac and Windows.**

Tone curves, warmth, gamma, contrast and saturation for every display you own, with hold-to-compare.

[Download the latest build](../../releases/latest) · [Mac](#screentune-for-mac) · [Windows](#screentune-for-windows)

</div>

## ScreenTune for Mac

<img src="docs/media/screentune-mac.webp" width="100%" alt="ScreenTune for Mac: the menu bar panel opens, dragging Warmth warms the whole screen live, the Black tone point lifts red in the shadows, each display keeps its own settings, and holding the eye button shows the original colors." />

Lives in the menu bar. Each display keeps its own settings, and holding the eye button shows the original colors. macOS 26 or later. Source in [`mac/`](mac/) (Swift, SwiftUI + AppKit).

## ScreenTune for Windows

<img src="docs/media/screentune-windows.webp" width="100%" alt="ScreenTune for Windows: the flyout rises from the taskbar, five taps on the plus button move Gamma from 2.200 to 2.205, dragging Warmth warms the desktop, each monitor keeps its own settings, and holding the eye button shows the original colors." />

Lives in the notification area. Every slider has − / + buttons that move by 0.001 (warmth by 1K), on top of drag, arrow keys and the mouse wheel. Windows 10 (19041) or later. Source in [`windows/`](windows/) (C#, .NET 9, WinUI 3).

## Downloads

Every push to `master` builds both apps and publishes them on the [Releases](../../releases) page as "ScreenTune build N", with `ScreenTune-macOS.zip` and `ScreenTune-Windows-x64.zip`. Pushes that only change Markdown files or `docs/` do not build.

The Mac build is not signed (no Apple Developer account). macOS blocks the first launch once: open it, then System Settings > Privacy & Security > Open Anyway, or run `xattr -dr com.apple.quarantine /Applications/ScreenTune.app`.

## Build locally

| App | Folder | Stack | Guide |
|---|---|---|---|
| Mac | [`mac/`](mac/) | Swift, SwiftUI + AppKit, Xcode 26 | [`mac/CLAUDE.md`](mac/CLAUDE.md) |
| Windows | [`windows/`](windows/) | C#, .NET 9, WinUI 3 | [`windows/README.md`](windows/README.md) |

The films above are HyperFrames compositions in [`docs/showcase/`](docs/showcase/).
