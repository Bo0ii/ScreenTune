import AppKit
import Combine
import SwiftUI

/// Menu bar icon plus a custom frosted panel. Replaces MenuBarExtra, whose window
/// background, sizing and dismissal can't be controlled.
@MainActor
final class StatusBarController: NSObject, NSWindowDelegate {
    private let statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
    private let panel = MenuPanel()
    private var outsideClickMonitor: Any?
    private var lastClose = Date.distantPast
    private var cancellables = Set<AnyCancellable>()

    init(calibration: CalibrationModel) {
        super.init()

        let content = ContentView(calibration: calibration) { [weak self] in self?.close() }
        panel.setContent(NSHostingView(rootView: content))
        panel.delegate = self
        panel.onCancel = { [weak self] in self?.close() }

        if let button = statusItem.button {
            button.image = NSImage(systemSymbolName: "slider.horizontal.3", accessibilityDescription: "ScreenTune")
            button.target = self
            button.action = #selector(togglePanel)
            // Open on mouse down like system menus, not on release.
            button.sendAction(on: [.leftMouseDown, .rightMouseDown])
        }

        calibration.$isEnabled
            .sink { [weak self] on in self?.statusItem.button?.appearsDisabled = !on }
            .store(in: &cancellables)
    }

    @objc private func togglePanel() {
        if panel.isVisible {
            close()
        } else if Date().timeIntervalSince(lastClose) > 0.2 {
            // Clicking the icon while open first resigns the panel (closing it), then
            // fires this action; the delay stops it from reopening straight away.
            open()
        }
    }

    private func open() {
        guard let button = statusItem.button, let buttonWindow = button.window else { return }
        let anchor = buttonWindow.convertToScreen(button.convert(button.bounds, to: nil))
        let visible = (buttonWindow.screen ?? NSScreen.main)?.visibleFrame ?? .zero
        let size = panel.fittingContentSize()

        let x = min(max(anchor.midX - size.width / 2, visible.minX + 8), visible.maxX - size.width - 8)
        let y = max(anchor.minY - size.height - 5, visible.minY + 8)
        panel.setFrame(NSRect(x: x, y: y, width: size.width, height: size.height), display: true)
        panel.invalidateShadow()

        panel.alphaValue = 0
        panel.makeKeyAndOrderFront(nil)
        NSAnimationContext.runAnimationGroup { context in
            context.duration = 0.12
            panel.animator().alphaValue = 1
        }
        button.highlight(true)

        outsideClickMonitor = NSEvent.addGlobalMonitorForEvents(matching: [.leftMouseDown, .rightMouseDown]) { [weak self] _ in
            MainActor.assumeIsolated { self?.close() }
        }
    }

    func close() {
        guard panel.isVisible else { return }
        panel.orderOut(nil)
        statusItem.button?.highlight(false)
        if let monitor = outsideClickMonitor {
            NSEvent.removeMonitor(monitor)
            outsideClickMonitor = nil
        }
        lastClose = Date()
    }

    func windowDidResignKey(_ notification: Notification) {
        close()
    }
}

/// Borderless, rounded, frosted panel that behaves like a menu bar menu.
final class MenuPanel: NSPanel {
    var onCancel: (() -> Void)?

    private let effectView = NSVisualEffectView()
    private static let cornerRadius: CGFloat = 16

    init() {
        super.init(contentRect: .zero, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: true)
        isOpaque = false
        backgroundColor = .clear
        hasShadow = true
        level = .popUpMenu
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .transient, .ignoresCycle]
        isReleasedWhenClosed = false
        hidesOnDeactivate = false
        isMovable = false
        animationBehavior = .none

        // Classic frosted blur of whatever is behind the panel, adapting to light/dark.
        effectView.material = .popover
        effectView.blendingMode = .behindWindow
        effectView.state = .active
        // A mask image (not a layer corner radius) keeps the blur, the vibrancy and
        // the window shadow all following the rounded shape.
        effectView.maskImage = Self.roundedMask(radius: Self.cornerRadius)
        contentView = effectView
    }

    func setContent(_ view: NSView) {
        view.translatesAutoresizingMaskIntoConstraints = false
        effectView.addSubview(view)
        NSLayoutConstraint.activate([
            view.leadingAnchor.constraint(equalTo: effectView.leadingAnchor),
            view.trailingAnchor.constraint(equalTo: effectView.trailingAnchor),
            view.topAnchor.constraint(equalTo: effectView.topAnchor),
            view.bottomAnchor.constraint(equalTo: effectView.bottomAnchor),
        ])
    }

    func fittingContentSize() -> NSSize {
        effectView.subviews.first?.fittingSize ?? .zero
    }

    // Borderless windows refuse key status by default; the panel needs it for
    // keyboard shortcuts, Esc and the screen menu.
    override var canBecomeKey: Bool { true }

    override func cancelOperation(_ sender: Any?) {
        onCancel?()
    }

    private static func roundedMask(radius: CGFloat) -> NSImage {
        let edge = radius * 2 + 1
        let image = NSImage(size: NSSize(width: edge, height: edge), flipped: false) { rect in
            NSColor.black.setFill()
            NSBezierPath(roundedRect: rect, xRadius: radius, yRadius: radius).fill()
            return true
        }
        image.capInsets = NSEdgeInsets(top: radius, left: radius, bottom: radius, right: radius)
        image.resizingMode = .stretch
        return image
    }
}
