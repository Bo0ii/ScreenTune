import Cocoa
import CoreImage
import QuartzCore

/// Full-screen overlay windows that apply a CIColorControls saturation filter
/// to all content behind them. Uses CABackdropLayer — the same private class
/// that NSVisualEffectView uses — which has special window-server support for
/// capturing cross-app, cross-window background content. Falls back to
/// CALayer.backgroundFilters if the class is unavailable.
@MainActor
final class SaturationOverlay {
    private var overlayWindows: [NSWindow] = []
    private var filterLayers:   [CALayer]  = []
    private var ciFilter: CIFilter?

    func update(saturation: Double) {
        guard abs(saturation - 1.0) > 0.004 else {
            teardown(); return
        }
        let f = CIFilter(name: "CIColorControls")!
        f.setDefaults()
        f.setValue(Float(saturation), forKey: kCIInputSaturationKey)
        ciFilter = f
        ensureSetup()
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        filterLayers.forEach { $0.filters = [f] }
        CATransaction.commit()
        overlayWindows.forEach { $0.orderFront(nil) }
    }

    func teardown() {
        overlayWindows.forEach { $0.orderOut(nil) }
        overlayWindows.removeAll()
        filterLayers.removeAll()
        ciFilter = nil
    }

    private func ensureSetup() {
        guard overlayWindows.isEmpty else { return }
        for screen in NSScreen.screens {
            let (win, layer) = makeWindow(screen: screen)
            overlayWindows.append(win)
            if let layer { filterLayers.append(layer) }
        }
    }

    private func makeWindow(screen: NSScreen) -> (NSWindow, CALayer?) {
        let win = NSWindow(
            contentRect: screen.frame,
            styleMask:   .borderless,
            backing:     .buffered,
            defer:       false,
            screen:      screen
        )
        win.isOpaque             = false
        win.backgroundColor      = .clear
        win.hasShadow            = false
        win.level                = NSWindow.Level(rawValue: 1000) // above all app windows so CABackdropLayer captures them
        win.ignoresMouseEvents   = true
        win.collectionBehavior   = [.canJoinAllSpaces, .stationary, .fullScreenAuxiliary]
        win.isReleasedWhenClosed = false

        let viewSize = screen.frame.size
        let view = NSView(frame: CGRect(origin: .zero, size: viewSize))
        view.wantsLayer = true
        view.layer?.isOpaque = false
        view.layer?.backgroundColor = CGColor.clear

        var filterLayer: CALayer?

        if let backdropClass = NSClassFromString("CABackdropLayer") as? CALayer.Type {
            // CABackdropLayer captures what's visually behind the window in the
            // window server compositor (including other apps and the desktop),
            // then applies its `filters` to that captured content.
            let backdrop = backdropClass.init()
            backdrop.frame = CGRect(origin: .zero, size: viewSize)
            backdrop.autoresizingMask = [.layerWidthSizable, .layerHeightSizable]
            view.layer?.addSublayer(backdrop)
            filterLayer = backdrop
        } else {
            // Fallback — works within the same window's compositor context only
            filterLayer = view.layer
        }

        win.contentView = view
        return (win, filterLayer)
    }
}
