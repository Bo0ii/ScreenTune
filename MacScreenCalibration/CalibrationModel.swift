import SwiftUI
import Combine
import CoreGraphics

// Bridges a Darwin (CFNotificationCenter) notification to a Swift closure.
// Holds a strong reference to itself via the observer properties on CalibrationModel.
private final class _DarwinObserver {
    private let center = CFNotificationCenterGetDarwinNotifyCenter()
    private let cfName: CFNotificationName
    private let handler: () -> Void

    init(name: String, handler: @escaping () -> Void) {
        cfName = CFNotificationName(name as CFString)
        self.handler = handler
        CFNotificationCenterAddObserver(
            center,
            Unmanaged.passUnretained(self).toOpaque(),
            _DarwinObserver.cfCallback,
            cfName.rawValue,
            nil,
            .deliverImmediately
        )
    }

    deinit {
        CFNotificationCenterRemoveObserver(
            center,
            Unmanaged.passUnretained(self).toOpaque(),
            cfName,
            nil
        )
    }

    private static let cfCallback: CFNotificationCallback = { _, observer, _, _, _ in
        guard let observer else { return }
        Unmanaged<_DarwinObserver>.fromOpaque(observer).takeUnretainedValue().handler()
    }
}

struct CalibrationProfile: Codable {
    var blackRed, blackGreen, blackBlue: Double
    var midRed, midGreen, midBlue: Double
    var whiteRed, whiteGreen, whiteBlue: Double
    var gamma, saturation, contrast: Double
}

@MainActor
class CalibrationModel: ObservableObject {
    @Published var blackRed: Double = UserDefaults.standard.object(forKey: "blackRed") as? Double ?? 0.0 {
        didSet { UserDefaults.standard.set(blackRed, forKey: "blackRed") }
    }
    @Published var blackGreen: Double = UserDefaults.standard.object(forKey: "blackGreen") as? Double ?? 0.0 {
        didSet { UserDefaults.standard.set(blackGreen, forKey: "blackGreen") }
    }
    @Published var blackBlue: Double = UserDefaults.standard.object(forKey: "blackBlue") as? Double ?? 0.0 {
        didSet { UserDefaults.standard.set(blackBlue, forKey: "blackBlue") }
    }

    @Published var midRed: Double = UserDefaults.standard.object(forKey: "midRed") as? Double ?? 1.0 {
        didSet { UserDefaults.standard.set(midRed, forKey: "midRed") }
    }
    @Published var midGreen: Double = UserDefaults.standard.object(forKey: "midGreen") as? Double ?? 1.0 {
        didSet { UserDefaults.standard.set(midGreen, forKey: "midGreen") }
    }
    @Published var midBlue: Double = UserDefaults.standard.object(forKey: "midBlue") as? Double ?? 1.0 {
        didSet { UserDefaults.standard.set(midBlue, forKey: "midBlue") }
    }

    @Published var whiteRed: Double = UserDefaults.standard.object(forKey: "whiteRed") as? Double ?? 1.0 {
        didSet { UserDefaults.standard.set(whiteRed, forKey: "whiteRed") }
    }
    @Published var whiteGreen: Double = UserDefaults.standard.object(forKey: "whiteGreen") as? Double ?? 1.0 {
        didSet { UserDefaults.standard.set(whiteGreen, forKey: "whiteGreen") }
    }
    @Published var whiteBlue: Double = UserDefaults.standard.object(forKey: "whiteBlue") as? Double ?? 1.0 {
        didSet { UserDefaults.standard.set(whiteBlue, forKey: "whiteBlue") }
    }

    @Published var gamma: Double = UserDefaults.standard.object(forKey: "gamma") as? Double ?? 2.2 {
        didSet { UserDefaults.standard.set(gamma, forKey: "gamma") }
    }
    @Published var saturation: Double = UserDefaults.standard.object(forKey: "saturation") as? Double ?? 1.0 {
        didSet { UserDefaults.standard.set(saturation, forKey: "saturation") }
    }
    @Published var contrast: Double = UserDefaults.standard.object(forKey: "contrast") as? Double ?? 1.0 {
        didSet { UserDefaults.standard.set(contrast, forKey: "contrast") }
    }

    @Published var hasUnappliedChanges = false

    private var cancellables        = Set<AnyCancellable>()
    private let saturationOverlay   = SaturationOverlay()
    private var deskrivIsDimming    = false
    private var dimObserver:        _DarwinObserver?
    private var restoreObserver:    _DarwinObserver?
    private var reapplyTimer:       Timer?
    private var mouseMonitor:       Any?
    private var lastMouseActivity:  Date = Date()

    private var appliedGammaR:     [CGGammaValue] = []
    private var appliedGammaG:     [CGGammaValue] = []
    private var appliedGammaB:     [CGGammaValue] = []
    private var appliedSaturation: Double = 1.0

    init() {
        // Re-apply persisted state on launch
        Task { @MainActor [weak self] in
            guard let self else { return }
            self.applyAll()
        }

        // Mark pending changes when any slider is edited
        Publishers.MergeMany(
            $blackRed.dropFirst().map   { _ in () },
            $blackGreen.dropFirst().map { _ in () },
            $blackBlue.dropFirst().map  { _ in () },
            $midRed.dropFirst().map     { _ in () },
            $midGreen.dropFirst().map   { _ in () },
            $midBlue.dropFirst().map    { _ in () },
            $whiteRed.dropFirst().map   { _ in () },
            $whiteGreen.dropFirst().map { _ in () },
            $whiteBlue.dropFirst().map  { _ in () },
            $gamma.dropFirst().map      { _ in () },
            $contrast.dropFirst().map   { _ in () },
            $saturation.dropFirst().map { _ in () }
        )
        .sink { [weak self] in self?.hasUnappliedChanges = true }
        .store(in: &cancellables)

        // ── Mouse presence tracking for KVM detection ────────────────────────────
        // When the user switches KVM away the Mac stops receiving HID events.
        // We stop the drift-correction timer while the user is absent, and trigger
        // an immediate re-apply the moment they move the mouse back.
        mouseMonitor = NSEvent.addGlobalMonitorForEvents(matching: [.mouseMoved, .leftMouseDown, .rightMouseDown, .scrollWheel]) { [weak self] _ in
            Task { @MainActor [weak self] in
                guard let self else { return }
                let wasAway = Date().timeIntervalSince(self.lastMouseActivity) > 10.0
                self.lastMouseActivity = Date()
                if wasAway && self.displayGammaDrifted() {
                    self.reapplyStoredGamma()
                }
            }
        }

        // ── Display config changes (display add/remove, resolution change) ──────
        // Guard against deskrivIsDimming so we don't fight DeskRiv's dim overlay.
        NotificationCenter.default
            .publisher(for: NSApplication.didChangeScreenParametersNotification)
            .debounce(for: .milliseconds(500), scheduler: RunLoop.main)
            .sink { [weak self] _ in
                guard let self, !self.deskrivIsDimming else { return }
                self.reapplyStoredGamma()
                self.saturationOverlay.teardown()
                self.saturationOverlay.update(saturation: self.appliedSaturation)
            }
            .store(in: &cancellables)

        NSWorkspace.shared.notificationCenter
            .publisher(for: NSWorkspace.screensDidWakeNotification)
            .debounce(for: .milliseconds(500), scheduler: RunLoop.main)
            .sink { [weak self] _ in
                guard let self, !self.deskrivIsDimming else { return }
                self.reapplyStoredGamma()
                self.saturationOverlay.update(saturation: self.appliedSaturation)
            }
            .store(in: &cancellables)

        NSWorkspace.shared.notificationCenter
            .publisher(for: NSWorkspace.didWakeNotification)
            .debounce(for: .milliseconds(500), scheduler: RunLoop.main)
            .sink { [weak self] _ in
                guard let self, !self.deskrivIsDimming else { return }
                self.reapplyStoredGamma()
                self.saturationOverlay.update(saturation: self.appliedSaturation)
            }
            .store(in: &cancellables)

        // ── DeskRiv KVM integration ──────────────────────────────────────────────
        // DeskRiv posts these Darwin notifications from its macOS display_dim module.
        // "displayDimmed"   → DeskRiv applied CGSetDisplayTransferByFormula; pause our timer.
        // "displayRestored" → DeskRiv called CGDisplayRestoreColorSyncSettings; re-apply now.
        dimObserver = _DarwinObserver(name: "com.deskriv.displayDimmed") { [weak self] in
            Task { @MainActor [weak self] in self?.deskrivIsDimming = true }
        }
        restoreObserver = _DarwinObserver(name: "com.deskriv.displayRestored") { [weak self] in
            Task { @MainActor [weak self] in
                guard let self else { return }
                self.deskrivIsDimming = false
                self.reapplyStoredGamma()
            }
        }

        // ── 2-second periodic timer ──────────────────────────────────────────────
        // Same pattern as DeskRiv's own `ensure_refresh_thread` (also 2 s).
        // Catches macOS gamma resets on display wake, Night Shift, True Tone, screen saver.
        // Skips when DeskRiv is actively dimming to avoid fighting its gamma formula.
        reapplyTimer = Timer.scheduledTimer(withTimeInterval: 2.0, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { [weak self] in
                guard let self, !self.deskrivIsDimming else { return }
                // Skip while user is away (KVM switched to another machine).
                // The mouse-event monitor will re-apply immediately on return.
                guard Date().timeIntervalSince(self.lastMouseActivity) < 10.0 else { return }
                if self.displayGammaDrifted() { self.reapplyStoredGamma() }
            }
        }
    }

    deinit {
        reapplyTimer?.invalidate()
        if let m = mouseMonitor { NSEvent.removeMonitor(m) }
        dimObserver     = nil   // _DarwinObserver.deinit removes the CF observer
        restoreObserver = nil
        Task { @MainActor [saturationOverlay] in saturationOverlay.teardown() }
        CGDisplayRestoreColorSyncSettings()
    }

    // MARK: - Live display via CGSetDisplayTransferByTable

    func applyToDisplay() {
        let displayID = CGMainDisplayID()
        let n = 256
        var r = [CGGammaValue](repeating: 0, count: n)
        var g = [CGGammaValue](repeating: 0, count: n)
        var b = [CGGammaValue](repeating: 0, count: n)
        for i in 0..<n {
            let t = Double(i) / Double(n - 1)
            r[i] = CGGammaValue(liveChannel(t, black: blackRed,   mid: midRed,   white: whiteRed))
            g[i] = CGGammaValue(liveChannel(t, black: blackGreen, mid: midGreen, white: whiteGreen))
            b[i] = CGGammaValue(liveChannel(t, black: blackBlue,  mid: midBlue,  white: whiteBlue))
        }
        CGSetDisplayTransferByTable(displayID, UInt32(n), &r, &g, &b)
        appliedGammaR = r; appliedGammaG = g; appliedGammaB = b
    }

    func applyAll() {
        applyToDisplay()
        saturationOverlay.update(saturation: saturation)
        appliedSaturation = saturation
        hasUnappliedChanges = false
    }

    private func reapplyStoredGamma() {
        guard !appliedGammaR.isEmpty else { return }
        var r = appliedGammaR, g = appliedGammaG, b = appliedGammaB
        CGSetDisplayTransferByTable(CGMainDisplayID(), UInt32(r.count), &r, &g, &b)
    }

    private func displayGammaDrifted() -> Bool {
        guard !appliedGammaR.isEmpty else { return false }
        let n = appliedGammaR.count
        var cr = [CGGammaValue](repeating: 0, count: n)
        var cg = [CGGammaValue](repeating: 0, count: n)
        var cb = [CGGammaValue](repeating: 0, count: n)
        var count: UInt32 = 0
        guard CGGetDisplayTransferByTable(CGMainDisplayID(), UInt32(n), &cr, &cg, &cb, &count) == .success,
              count == UInt32(n) else { return false }
        let tol: CGGammaValue = 0.02
        for i in 0..<n {
            if abs(cr[i]-appliedGammaR[i]) > tol ||
               abs(cg[i]-appliedGammaG[i]) > tol ||
               abs(cb[i]-appliedGammaB[i]) > tol { return true }
        }
        return false
    }

    func restoreDisplay() {
        CGDisplayRestoreColorSyncSettings()
        saturationOverlay.teardown()
    }

    private func liveChannel(_ t: Double, black: Double, mid: Double, white: Double) -> Double {
        var v = pow(max(t, 0), 2.2)
        v = applyContrast(v)
        v = applyTone(v, black: black, mid: mid, white: white)
        v = pow(max(v, 0), 1.0 / gamma)
        return clamp(v)
    }

    // MARK: - Preview pipeline (tone/gamma/contrast only — saturation is display-level overlay)

    func apply(to color: Color) -> Color {
        var r: CGFloat = 0, g: CGFloat = 0, b: CGFloat = 0, a: CGFloat = 0
        NSColor(color).usingColorSpace(.sRGB)?.getRed(&r, green: &g, blue: &b, alpha: &a)
        var rd = Double(r), gd = Double(g), bd = Double(b)
        (rd, gd, bd) = applyRGB(r: rd, g: gd, b: bd)
        return Color(red: rd, green: gd, blue: bd)
    }

    func applyGray(_ t: Double) -> Double {
        var r = t, g = t, b = t
        (r, g, b) = applyRGB(r: r, g: g, b: b)
        return (r + g + b) / 3.0
    }

    func applyRGB(r: Double, g: Double, b: Double) -> (Double, Double, Double) {
        var r = r, g = g, b = b
        r = pow(max(r, 0), 2.2); g = pow(max(g, 0), 2.2); b = pow(max(b, 0), 2.2)
        r = applyContrast(r);    g = applyContrast(g);    b = applyContrast(b)
        r = applyTone(r, black: blackRed,   mid: midRed,   white: whiteRed)
        g = applyTone(g, black: blackGreen, mid: midGreen, white: whiteGreen)
        b = applyTone(b, black: blackBlue,  mid: midBlue,  white: whiteBlue)
        let enc = 1.0 / gamma
        r = pow(max(r, 0), enc); g = pow(max(g, 0), enc); b = pow(max(b, 0), enc)
        return (clamp(r), clamp(g), clamp(b))
    }

    // MARK: - Profile export / import

    func exportProfile() -> CalibrationProfile {
        CalibrationProfile(blackRed: blackRed, blackGreen: blackGreen, blackBlue: blackBlue,
                           midRed: midRed, midGreen: midGreen, midBlue: midBlue,
                           whiteRed: whiteRed, whiteGreen: whiteGreen, whiteBlue: whiteBlue,
                           gamma: gamma, saturation: saturation, contrast: contrast)
    }

    func importProfile(_ p: CalibrationProfile) {
        blackRed = p.blackRed; blackGreen = p.blackGreen; blackBlue = p.blackBlue
        midRed = p.midRed;     midGreen = p.midGreen;     midBlue = p.midBlue
        whiteRed = p.whiteRed; whiteGreen = p.whiteGreen; whiteBlue = p.whiteBlue
        gamma = p.gamma; saturation = p.saturation; contrast = p.contrast
        saturationOverlay.teardown()
        applyAll()
    }

    // MARK: - Reset

    func reset() {
        blackRed = 0; blackGreen = 0; blackBlue = 0
        midRed = 1;   midGreen = 1;   midBlue = 1
        whiteRed = 1; whiteGreen = 1; whiteBlue = 1
        gamma = 2.2; saturation = 1.0; contrast = 1.0
        restoreDisplay()
        appliedGammaR = []; appliedGammaG = []; appliedGammaB = []
        appliedSaturation = 1.0
        hasUnappliedChanges = false
    }

    // MARK: - Shared helpers

    private func applyContrast(_ v: Double) -> Double {
        guard abs(contrast - 1.0) > 0.001 else { return v }
        return clamp(0.5 + (v - 0.5) * contrast)
    }

    private func applyTone(_ v: Double, black: Double, mid: Double, white: Double) -> Double {
        var out = black + v * (1.0 - black)
        out = pow(max(out, 0), 1.0 / mid)
        return clamp(out * white)
    }

    private func clamp(_ v: Double) -> Double { max(0.0, min(1.0, v)) }
}
