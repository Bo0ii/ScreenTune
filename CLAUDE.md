# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

ScreenTune for Mac and Windows in one repo. Each app has its own `CLAUDE.md` with build steps, architecture and gotchas; read the one for the app you are working on.

| Folder | App | Guide |
|---|---|---|
| `mac/` | Swift, SwiftUI + AppKit, Xcode 26, macOS 26+ | `mac/CLAUDE.md` |
| `windows/` | C# 13, .NET 9, WinUI 3 | `windows/CLAUDE.md` |

The two apps share the same 13 values, ranges, math and profile JSON. A change to the values or the math in one app should be mirrored in the other so profiles keep moving between them.

## CI and releases

`.github/workflows/build.yml` runs on every push to `master` (pushes that only touch Markdown or `docs/` are skipped) and on manual dispatch:

1. **Windows** (`windows-latest`): `dotnet test`, then `windows/tools/publish.ps1` (self-contained, trimmed, x64), zipped as `ScreenTune-Windows-x64.zip`.
2. **macOS** (`macos-26`, default Xcode): `mac/Tests/run.sh`, then `xcodebuild -target ScreenTune -configuration Release` as a universal binary, ad-hoc signed (`CODE_SIGN_IDENTITY=-`, no Apple Developer account), zipped with `ditto` as `ScreenTune-macOS.zip`.
3. **Release** (`ubuntu-latest`): `gh release create build-<run number>` with both zips.

- The repo is private: Mac runner minutes count 10x against the monthly Actions allowance, Windows 2x. A full run takes about 4 minutes, roughly 20 billed minutes (Mac job about 1 min x10, Windows about 4 min x2).
- The Mac project has no shared scheme, so CI builds with `-target`, not `-scheme`.
- Mac bundle ID is still `com.example.ScreenTune` (see `mac/CLAUDE.md` before changing it: saved settings are keyed by it).

## README films

`README.md` shows one looping product film per app: `docs/media/screentune-mac.webp` and `docs/media/screentune-windows.webp` (1200x675, 25 fps, 18 s, about 5 MB each). Their sources are HyperFrames compositions, one project per film: `docs/showcase/mac/index.html` and `docs/showcase/windows/index.html`. Look, palette and motion rules: `docs/showcase/DESIGN.md`.

Regenerate one (from its project folder; `mac` shown):

```bash
npx --yes hyperframes@0.9.4 check                       # lint + layout; the scrim "text_occluded" findings are expected
npx --yes hyperframes@0.9.4 snapshot --at 5,8,12 --no-end -o snapshots
npx --yes hyperframes@0.9.4 render --fps 25 --quality delivery -o renders/mac.mp4
ffmpeg -i renders/mac.mp4 -vf "fps=25,scale=1200:-1:flags=lanczos" -c:v libwebp_anim -lossless 0 \
    -quality 80 -compression_level 6 -loop 0 -an ../../media/screentune-mac.webp
```

- Animated WebP, not GIF: the camera moves change every pixel, and the same film as a GIF was 69 MB (WebP: under 5 MB, full color, no banding).
- The UI in the films is redrawn from the app sources. If a panel changes (rows, labels, ranges, value formats), update the film so it stays truthful.
- `shared.css` / `shared.js` are copied into both project folders (HyperFrames only serves files inside the project). Keep the copies identical.
- Camera targets are measured from the CSS layout at load (`ST.pos` / `ST.cam`), so moving UI elements does not need hand-tuned coordinates.
- Slider knobs move with `x` / `scaleX`, not `left` / `width` (HyperFrames rejects layout-property tweens), and initial states use `gsap.set` so frame 0 is right.
- The Windows film uses Segoe UI Variable through `@font-face { src: local(...) }`, so render it on Windows.
