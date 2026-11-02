<div align="center">

# ScreenTune

**Per-screen color tuning for Mac and Windows.**

Tone curves, warmth, gamma, contrast and saturation for every display you own, with hold-to-compare.

[Download the latest build](../../releases/latest)

</div>

<img src="docs/media/screentune.webp" width="100%" alt="ScreenTune: the panel opens, dragging Warmth warms the whole screen live, five taps on the plus button move Red from 0.000 to 0.005, each display keeps its own settings, and holding the eye button shows the original colors." />

## What it does

Both apps have the same controls, steps and math, and share profile files.

- **Per screen:** every display keeps its own settings.
- **Tone:** black, middle and white points for each color channel.
- **Adjust:** warmth, gamma, contrast and saturation.
- **Fine-tune:** every slider has − and + buttons that move 0.001 (warmth 1K) and repeat while held. Drags land on the same steps, so the number shown is the exact value.
- **Hold to compare:** hold the eye button to see the original colors.

| | Mac | Windows |
|---|---|---|
| Lives in | Menu bar | Notification area (taskbar) |
| Needs | macOS 26 or later | Windows 10 (19041) or later, x64 |
| Download | `ScreenTune.dmg` | `ScreenTune-Setup.exe` |
| Source | [`mac/`](mac/) (Swift, SwiftUI + AppKit) | [`windows/`](windows/) (C#, .NET 9, WinUI 3) |

## Downloads

Every push to `master` builds both apps and publishes them on the [Releases](../../releases) page as "ScreenTune build N". Pushes that only change Markdown files or `docs/` do not build.

- **Mac:** open `ScreenTune.dmg` and drag ScreenTune to Applications. The app is not signed (no Apple Developer account), so macOS blocks the first launch once: open it, then System Settings > Privacy & Security > Open Anyway, or run `xattr -dr com.apple.quarantine /Applications/ScreenTune.app`.
- **Windows:** run `ScreenTune-Setup.exe` (no admin rights needed). It is not signed, so SmartScreen may warn: More info > Run anyway.
