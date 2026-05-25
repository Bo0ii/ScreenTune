# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build

Build and run via Xcode only — open `MacScreenCalibration.xcodeproj`. No CLI build target exists.

- **Deployment target**: macOS 13.0+
- **Language**: Swift 5.0, SwiftUI
- **Bundle ID**: `com.example.MacScreenCalibration` — change before any release

## Architecture

Menubar-only app (`MenuBarExtra` / `.window` style). No main window. Single shared `CalibrationModel` instance created in `MacScreenCalibrationApp` and passed down.

### Two separate display pipelines

**Gamma/tone/contrast** — `CalibrationModel.applyToDisplay()` writes a 256-entry LUT per channel via `CGSetDisplayTransferByTable`. The pipeline per channel: linearize (gamma 2.2 decode) → contrast → tone (black/mid/white points) → re-encode (1/gamma). Operates on `CGMainDisplayID()` only.

**Saturation** — `SaturationOverlay` creates one borderless `NSWindow` per screen at window level 1000 (above all apps), with a `CABackdropLayer` (same private class NSVisualEffectView uses) that captures the compositor backbuffer and applies a `CIColorControls` saturation filter. Falls back to `CALayer.backgroundFilters` if `CABackdropLayer` is unavailable. Saturation = 1.0 tears down all overlay windows entirely.

### Drift correction

macOS resets gamma after Night Shift, True Tone, screen saver, display wake. Three recovery paths:
1. **2-second `Timer`** — checks `displayGammaDrifted()` (tolerance 0.02) and re-applies if drifted. Skips when user is away (no mouse activity > 10 s) to avoid fighting KVM switches.
2. **Mouse return** — `NSEvent.addGlobalMonitorForEvents` detects first mouse move after 10 s absence and triggers immediate re-apply.
3. **System notifications** — `NSApplication.didChangeScreenParametersNotification`, `screensDidWakeNotification`, `didWakeNotification` all trigger re-apply (debounced 500 ms).

### DeskRiv KVM integration

Listens for Darwin notifications `com.deskriv.displayDimmed` / `com.deskriv.displayRestored` via `CFNotificationCenterGetDarwinNotifyCenter()`. Sets `deskrivIsDimming = true` while DeskRiv owns the gamma formula; all recovery paths check this flag before writing.

### State persistence

Every `@Published` property on `CalibrationModel` writes to `UserDefaults` in its `didSet`. Settings are re-applied on launch via a `Task` in `init()`. Profile export/import uses `Codable` JSON (`CalibrationProfile` struct).

### Settings window

`SettingsWindowManager.shared` manages a singleton `NSWindow` hosting `SettingsView`. Opened from the gear button in `ContentView`. Handles profile import/export (JSON via `NSSavePanel`/`NSOpenPanel`) and Launch at Login via `SMAppService.mainApp`.
