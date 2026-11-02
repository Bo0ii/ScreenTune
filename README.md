# ScreenTune

Per-screen display tuning for Mac and Windows: tone curves (black, middle and white points per color channel), warmth, gamma, contrast and saturation, with an on/off switch and hold-to-compare. Both apps share the same controls, math and profile files.

| Folder | App | Stack |
|---|---|---|
| [`mac/`](mac/) | Menu bar app, macOS 26+ | Swift, SwiftUI + AppKit, Xcode 26 |
| [`windows/`](windows/) | Notification-area app, Windows 10 19041+ | C#, .NET 9, WinUI 3 |

## Downloads

Every push to `master` builds both apps and publishes them on the [Releases](../../releases) page as "ScreenTune build N", with `ScreenTune-macOS.zip` and `ScreenTune-Windows-x64.zip`. Pushes that only change Markdown files do not build.

The Mac build is not signed (no Apple Developer account). macOS blocks the first launch once: open it, then System Settings > Privacy & Security > Open Anyway, or run `xattr -dr com.apple.quarantine /Applications/ScreenTune.app`.

## Build locally

See [`mac/CLAUDE.md`](mac/CLAUDE.md) and [`windows/README.md`](windows/README.md).
