# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

ScreenTune for Mac and Windows in one repo. Each app has its own `CLAUDE.md` with build steps, architecture and gotchas; read the one for the app you are working on.

| Folder | App | Guide |
|---|---|---|
| `mac/` | Swift, SwiftUI + AppKit, Xcode 26, macOS 26+ | `mac/CLAUDE.md` |
| `windows/` | C# 13, .NET 9, WinUI 3 | `windows/CLAUDE.md` |

The two apps share the same 13 values, ranges, math and profile JSON. A change to the values or the math in one app should be mirrored in the other so profiles keep moving between them.

## CI and releases

`.github/workflows/build.yml` runs on every push to `master` (Markdown-only pushes skipped) and on manual dispatch:

1. **Windows** (`windows-latest`): `dotnet test`, then `windows/tools/publish.ps1` (self-contained, trimmed, x64), zipped as `ScreenTune-Windows-x64.zip`.
2. **macOS** (`macos-26`, default Xcode): `mac/Tests/run.sh`, then `xcodebuild -target ScreenTune -configuration Release` as a universal binary, ad-hoc signed (`CODE_SIGN_IDENTITY=-`, no Apple Developer account), zipped with `ditto` as `ScreenTune-macOS.zip`.
3. **Release** (`ubuntu-latest`): `gh release create build-<run number>` with both zips.

- The repo is private: Mac runner minutes count 10x against the monthly Actions allowance, Windows 2x. A full run is roughly an hour of billed minutes.
- The Mac project has no shared scheme, so CI builds with `-target`, not `-scheme`.
- Mac bundle ID is still `com.example.ScreenTune` (see `mac/CLAUDE.md` before changing it: saved settings are keyed by it).
