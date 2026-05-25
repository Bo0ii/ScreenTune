import Cocoa
import CoreImage
import QuartzCore

// Full-screen transparent windows at level 1000. Sets backgroundFilters on each
// window's layer-backed view so the Quartz compositor applies the CIColorControls
// saturation filter to every window below in the stack (cross-app, system-wide).
@MainActor
final class SaturationOverlay {
    private var overlayWindows: [NSWindow] = []
    private var filterLayers:   [CALayer]  = []

    func update(saturation: Double) {
        guard abs(saturation - 1.0) > 0.004 else {
            teardown(); return
        }
        let f = CIFilter(name: "CIColorControls")!
        f.setDefaults()
        f.setValue(Float(saturation), forKey: kCIInputSaturationKey)
        ensureSetup()
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        filterLayers.forEach { $0.backgroundFilters = [f] }
        CATransaction.commit()
        overlayWindows.forEach { $0.orderFront(nil) }
    }

    func teardown() {
        overlayWindows.forEach { $0.orderOut(nil) }
        overlayWindows.removeAll()
        filterLayers.removeAll()
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
        win.level                = NSWindow.Level(rawValue: 1000)
        win.ignoresMouseEvents   = true
        win.collectionBehavior   = [.canJoinAllSpaces, .stationary, .fullScreenAuxiliary]
        win.isReleasedWhenClosed = false

        let viewSize = screen.frame.size
        let view = NSView(frame: CGRect(origin: .zero, size: viewSize))
        view.wantsLayer                = true
        view.layerUsesCoreImageFilters = true   // required for backgroundFilters to work
        view.layer?.isOpaque           = false
        view.layer?.backgroundColor    = CGColor.clear

        win.contentView = view
        return (win, view.layer)
    }
}
