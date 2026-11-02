import AppKit
import QuartzCore

/// System-wide saturation, one overlay per screen.
///
/// Public API cannot filter other apps' pixels: `CALayer.backgroundFilters` only sees
/// this app's own layers, so it filters an empty transparent window and does nothing.
/// Instead each tuned screen gets a click-through window above everything holding a
/// `CABackdropLayer` (the private layer behind NSVisualEffectView). With
/// `windowServerAware` set, the WindowServer fills it with whatever is behind the
/// window and runs the private `colorSaturate` CAFilter on it, on the GPU, with no
/// capture permission needed.
@MainActor
final class SaturationOverlay {
    static let isSupported: Bool = backdropClass != nil && makeFilter() != nil

    private struct Overlay {
        let window: NSWindow
        let backdrop: CALayer
    }

    private var overlays: [CGDirectDisplayID: Overlay] = [:]
    private static let filterName = "saturate"
    private static var backdropClass: CALayer.Type? { NSClassFromString("CABackdropLayer") as? CALayer.Type }

    func set(_ amount: Double, on screen: NSScreen, display: CGDirectDisplayID) {
        guard Self.isSupported else { return }
        guard abs(amount - 1) >= DisplaySettings.saturationThreshold else {
            remove(display)
            return
        }
        guard let overlay = overlays[display] ?? makeOverlay(for: screen) else { return }
        overlays[display] = overlay

        // Updating the existing filter in place keeps slider drags cheap: no new
        // windows, layers or filters per tick.
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        overlay.backdrop.setValue(amount, forKeyPath: "filters.\(Self.filterName).inputAmount")
        CATransaction.commit()

        if !overlay.window.isVisible {
            overlay.window.orderFrontRegardless()
        }
    }

    func remove(_ display: CGDirectDisplayID) {
        overlays.removeValue(forKey: display)?.window.orderOut(nil)
    }

    func removeAll() {
        overlays.values.forEach { $0.window.orderOut(nil) }
        overlays.removeAll()
    }

    private func makeOverlay(for screen: NSScreen) -> Overlay? {
        guard let backdropClass = Self.backdropClass, let filter = Self.makeFilter() else { return nil }

        // No `screen:` argument: with it, contentRect is relative to that screen, and
        // passing the global frame would double the offset on secondary screens.
        let window = NSWindow(contentRect: screen.frame, styleMask: .borderless, backing: .buffered, defer: false)
        window.isOpaque = false
        window.backgroundColor = .clear
        window.hasShadow = false
        window.level = .screenSaver
        window.ignoresMouseEvents = true
        window.collectionBehavior = [.canJoinAllSpaces, .stationary, .fullScreenAuxiliary, .ignoresCycle]
        window.isReleasedWhenClosed = false
        window.isExcludedFromWindowsMenu = true
        window.animationBehavior = .none

        let view = NSView(frame: NSRect(origin: .zero, size: screen.frame.size))
        view.wantsLayer = true

        let backdrop = backdropClass.init()
        backdrop.frame = view.bounds
        backdrop.setValue(true, forKey: "windowServerAware")
        filter.setValue(Self.filterName, forKey: "name")
        backdrop.filters = [filter]
        view.layer?.addSublayer(backdrop)

        window.contentView = view
        return Overlay(window: window, backdrop: backdrop)
    }

    private static func makeFilter() -> NSObject? {
        guard let cls = NSClassFromString("CAFilter") as? NSObject.Type else { return nil }
        let selector = NSSelectorFromString("filterWithType:")
        guard cls.responds(to: selector) else { return nil }
        return cls.perform(selector, with: "colorSaturate")?.takeUnretainedValue() as? NSObject
    }
}
