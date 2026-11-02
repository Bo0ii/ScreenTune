# ScreenTune showcase films

Two short looping product films for the repo README, one per app. Rendered with HyperFrames, converted to GIF.

## Style Prompt

Calm, cinematic, Apple keynote product film. A near-black void, the product UI floating on a monitor, and a camera that glides in on the details that matter. Motion is soft and physical: long ease-in-outs for camera moves, quick spring-like settles for UI, blur-to-sharp reveals for type. Every animated value is a real app behavior (warmth slider, tone points, per-screen settings, hold-to-compare, 0.001 nudges). The UI is redrawn faithfully from the app source (`mac/ScreenTune/ContentView.swift`, `windows/src/ScreenTune/Views/*.xaml`), not invented.

## Colors

| Role | Hex | Use |
|---|---|---|
| Void | `#050507` | Frame background, start and end of every loop (seamless) |
| Ink | `#F5F5F7` | Headlines, captions |
| Muted | `#A1A1A6` | Secondary caption lines, "for Mac" / "for Windows" |
| Brand amber | `#FFA41B` | App icon gradient start, warm glow |
| Brand pink | `#FF3B6B` | App icon gradient middle |
| Brand indigo | `#5856D6` | App icon gradient end, cool glow |

The amber to pink to indigo gradient comes from the app icon (`mac/ScreenTune/AppIcon.icon/Assets/screen.svg`). It is the only gradient used on type, and only on one word per film.

App UI colors are the apps' own: macOS dark popover material and system tints; Windows dark acrylic, WinUI controls, channel tints `#E74856` / `#16C60C` / `#3A96DD`, warmth `#F7630C`, accent `#60CDFF`.

## Typography

- **Inter** for headlines, captions and the macOS UI. It stands in for SF Pro, which cannot be embedded; a deliberate exception to the "no Inter" default because the film shows real macOS UI.
- **Segoe UI Variable** for the Windows UI only, because that is what Windows draws.
- Headlines 120px / 700 / -0.045em. Captions 44px / 700 with a 28px / 400 muted second line. Numbers use tabular figures.

## Motion

- Camera: `power3.inOut`, 0.8 to 1.3 s, one move at a time.
- UI open/close: `expo.out` / `back.out(1.4)`, 0.25 to 0.45 s.
- Type: blur 14px to 0, y 30 to 0, staggered per word.
- Each film starts and ends on the void color so the GIF loops without a seam.

## What NOT to Do

- No real company logos or product marks (no Apple or Windows logos); menu bars, docks and taskbars use generic icons.
- No neon, no glow on UI elements, no drop shadows on buttons, no tinted pills behind text.
- No text over busy imagery without the bottom scrim.
- No invented features: only what the apps actually do.
