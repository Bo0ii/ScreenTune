# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

ScreenTune for Mac and Windows in one repo. Each app has its own `CLAUDE.md` with build steps, architecture and gotchas; read the one for the app you are working on.

| Folder | App | Guide |
|---|---|---|
| `mac/` | Swift, SwiftUI + AppKit, Xcode 26, macOS 26+ | `mac/CLAUDE.md` |
| `windows/` | C# 13, .NET 9, WinUI 3 | `windows/CLAUDE.md` |

## Keep the two apps the same

The apps share the same 13 values, ranges, math, profile JSON and fine-tune behavior. A change to any of these in one app must be mirrored in the other:

| What | Mac | Windows |
|---|---|---|
| Values, ranges, transfer table, warmth | `mac/ScreenTune/DisplaySettings.swift` | `windows/src/ScreenTune.Core/DisplaySettings.cs` |
| Fine steps (0.001, warmth 1K, grid snapping) | `Nudge` in `DisplaySettings.swift` | `windows/src/ScreenTune.Core/Nudge.cs` |
| Step tests (same cases) | `mac/Tests/main.swift` | `windows/tests/ScreenTune.Core.Tests/NudgeTests.cs` |
| Slider row (label, - / slider / +, value) | `SliderRow` in `mac/ScreenTune/ContentView.swift` | `windows/src/ScreenTune/Views/SliderRow.xaml(.cs)` |

Fine-tune, both apps: every slider has - / + buttons that move one step (0.001; warmth 1K) and repeat while held, drags snap to the same grid, values show 3 decimals and warmth exact Kelvin. Windows also steps with the mouse wheel and arrow keys; the Mac panel is a non-activating panel without keyboard focus and has no wheel stepping yet.

Platform-only by design: Mac has the DeskRiv KVM integration; Windows has the HDR note, Full-range curves (GDI gamma limit) and the tray right-click menu.

## CI and releases

`.github/workflows/build.yml` runs on every push to `master` (pushes that only touch Markdown or `docs/` are skipped) and on manual dispatch (`gh workflow run build.yml --ref <branch>`; only `master` publishes a release):

1. **Windows** (`windows-latest`): `dotnet test`, `windows/tools/publish.ps1` (self-contained, trimmed, x64), then Inno Setup (preinstalled on the image) builds `ScreenTune-Setup.exe` from `windows/installer/ScreenTune.iss`.
2. **macOS** (`macos-26`, default Xcode): `mac/Tests/run.sh`, `xcodebuild -target ScreenTune -configuration Release` as a universal binary, ad-hoc signed (`CODE_SIGN_IDENTITY=-`, no Apple Developer account), then `hdiutil` builds `ScreenTune.dmg` (app + Applications link).
3. **Release** (`ubuntu-latest`, `master` only): `gh release create build-<run number>` with exactly two files, `ScreenTune.dmg` and `ScreenTune-Setup.exe`.

- The installer is per-user (no admin), installs to `%LOCALAPPDATA%\Programs\ScreenTune` (same place as `publish.ps1 -Install`, so it upgrades either), quits a running copy with `--quit` first so the screens are restored, and removes the startup Run value on uninstall.
- The repo is private: Mac runner minutes count 10x against the monthly Actions allowance, Windows 2x. A full run is roughly 20 billed minutes.
- The Mac project has no shared scheme, so CI builds with `-target`, not `-scheme`.
- Mac bundle ID is still `com.example.ScreenTune` (see `mac/CLAUDE.md` before changing it: saved settings are keyed by it).
- Commits and releases carry no AI author or co-author lines.

## README film

`README.md` shows one looping product film for both apps: `docs/media/screentune.webp` (1200x675, 25 fps, 18.7 s, about 5 MB). It shows the Mac UI; the fine-tune caption covers both apps. Source: the HyperFrames project `docs/showcase/mac/` (`index.html`). Look, palette and motion rules: `docs/showcase/DESIGN.md`.

Regenerate (from `docs/showcase/mac/`):

```bash
npx --yes hyperframes@0.9.4 check                       # lint + layout; the scrim "text_occluded" findings are expected
npx --yes hyperframes@0.9.4 snapshot --at 5,10.9,14.2 --no-end -o snapshots
npx --yes hyperframes@0.9.4 render --fps 25 --quality delivery -o renders/mac.mp4
ffmpeg -i renders/mac.mp4 -vf "fps=25,scale=1200:-1:flags=lanczos" -c:v libwebp_anim -lossless 0 \
    -quality 80 -compression_level 6 -loop 0 -an ../../media/screentune.webp
```

- Animated WebP, not GIF: the camera moves change every pixel, and the film as a GIF was 69 MB (WebP: about 5 MB, full color, no banding).
- The UI in the film is redrawn from the Mac source. If the panel changes (rows, labels, ranges, value formats), update the film so it stays truthful.
- Camera targets are measured from the CSS layout at load (`ST.pos` / `ST.cam` in `shared.js`), so moving UI elements does not need hand-tuned coordinates.
- Slider knobs move with `x` / `scaleX`, not `left` / `width` (HyperFrames rejects layout-property tweens), and initial states use `gsap.set` so frame 0 is right.
