import AppKit
import Combine
import CoreGraphics

// Bridges a Darwin (CFNotificationCenter) notification to a Swift closure.
// Kept alive by CalibrationModel.darwinObservers.
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
        CFNotificationCenterRemoveObserver(center, Unmanaged.passUnretained(self).toOpaque(), cfName, nil)
    }

    private static let cfCallback: CFNotificationCallback = { _, observer, _, _, _ in
        guard let observer else { return }
        Unmanaged<_DarwinObserver>.fromOpaque(observer).takeUnretainedValue().handler()
    }
}

@MainActor
final class CalibrationModel: ObservableObject {
    struct Screen: Identifiable, Equatable {
        /// Display UUID. Stable across reconnects and reboots, unlike CGDirectDisplayID.
        let id: String
        let displayID: CGDirectDisplayID
        let name: String
        let isBuiltIn: Bool
    }

    @Published private(set) var screens: [Screen] = []
    @Published var selectedScreenID: String? {
        didSet { store.selectedScreenID = selectedScreenID }
    }
    @Published private var settings: [String: DisplaySettings] = [:] {
        didSet { settingsChanged(from: oldValue) }
    }

    /// Master switch. Off leaves every screen untouched but keeps the settings.
    @Published var isEnabled = true {
        didSet {
            guard isEnabled != oldValue else { return }
            store.isEnabled = isEnabled
            activeStateChanged()
        }
    }

    /// True while the user holds the compare button: shows the original colors.
    @Published var isComparing = false {
        didSet {
            guard isComparing != oldValue else { return }
            activeStateChanged()
        }
    }

    private var isActive: Bool { isEnabled && !isComparing }

    /// Settings of the screen picked in the popover.
    var current: DisplaySettings {
        get { selectedScreenID.flatMap { settings[$0] } ?? DisplaySettings() }
        set {
            guard let id = selectedScreenID else { return }
            settings[id] = newValue
        }
    }

    var selectedScreen: Screen? { screens.first { $0.id == selectedScreenID } }
    var saturationSupported: Bool { SaturationOverlay.isSupported }

    private let store = SettingsStore()
    private let gamma = GammaController()
    private let saturation = SaturationOverlay()

    private var pendingTone: Set<String> = []
    private var pendingSaturation: Set<String> = []
    private var flushScheduled = false

    private var deskrivIsDimming = false
    private var lastUserActivity = Date()
    private static let awayInterval: TimeInterval = 10

    private var cancellables = Set<AnyCancellable>()
    private var darwinObservers: [_DarwinObserver] = []
    private var eventMonitors: [Any] = []
    private var driftTimer: Timer?

    init() {
        screens = Self.currentScreens()
        var loaded = store.load()
        if loaded.isEmpty, let legacy = store.legacySettings() {
            // Old versions only ever tuned the main display.
            loaded[Self.uuid(for: CGMainDisplayID())] = legacy
            store.save(loaded)
        }
        settings = loaded
        selectedScreenID = validSelection(store.selectedScreenID)
        isEnabled = store.isEnabled

        applyEverything()
        observeSystem()
        observeDeskRiv()
        observeUserActivity()
        startDriftTimer()
    }

    // MARK: - Actions

    func resetCurrent() {
        current = DisplaySettings()
    }

    func importProfile(_ profile: DisplaySettings) {
        current = profile.clamped()
    }

    // MARK: - Applying

    private func settingsChanged(from old: [String: DisplaySettings]) {
        for id in Set(old.keys).union(settings.keys) {
            let before = old[id] ?? DisplaySettings()
            let after = settings[id] ?? DisplaySettings()
            if !before.hasSameTone(as: after) { pendingTone.insert(id) }
            if before.saturation != after.saturation { pendingSaturation.insert(id) }
        }
        scheduleFlush()
    }

    // Coalesces bursts (reset, import) into one display write and one disk write.
    // Slider drags still apply every tick because each tick is its own run loop pass.
    private func scheduleFlush() {
        guard !flushScheduled else { return }
        flushScheduled = true
        DispatchQueue.main.async { [weak self] in
            MainActor.assumeIsolated { self?.flush() }
        }
    }

    private func flush() {
        flushScheduled = false
        store.save(settings)
        let tone = pendingTone
        let sat = pendingSaturation
        pendingTone.removeAll()
        pendingSaturation.removeAll()
        for screen in screens {
            if tone.contains(screen.id) { applyTone(to: screen) }
            if sat.contains(screen.id) { applySaturation(to: screen) }
        }
    }

    /// What a screen should show right now: its settings, or untouched when off or comparing.
    private func effectiveSettings(for screen: Screen) -> DisplaySettings {
        isActive ? settings[screen.id] ?? DisplaySettings() : DisplaySettings()
    }

    private func applyTone(to screen: Screen) {
        // While DeskRiv dims, it owns the gamma tables. The new value is saved and
        // written when DeskRiv posts displayRestored.
        guard !deskrivIsDimming else { return }
        gamma.apply(effectiveSettings(for: screen), to: screen.displayID)
    }

    private func applySaturation(to screen: Screen) {
        guard let nsScreen = Self.nsScreen(for: screen.displayID) else { return }
        saturation.set(effectiveSettings(for: screen).saturation, on: nsScreen, display: screen.displayID)
    }

    private func activeStateChanged() {
        if isActive {
            applyEverything()
        } else {
            // One restore for all screens keeps hold-to-compare instant.
            if !deskrivIsDimming { gamma.removeAll() }
            saturation.removeAll()
        }
    }

    private func applyEverything() {
        screens.forEach { applyTone(to: $0); applySaturation(to: $0) }
    }

    // MARK: - Screens

    private func screensChanged() {
        screens = Self.currentScreens()
        let validated = validSelection(selectedScreenID)
        if validated != selectedScreenID { selectedScreenID = validated }
        gamma.retain(only: Set(screens.map(\.displayID)))
        // Frames may have moved or resized; rebuild overlays from scratch.
        saturation.removeAll()
        applyEverything()
    }

    private func validSelection(_ id: String?) -> String? {
        if let id, screens.contains(where: { $0.id == id }) { return id }
        let main = Self.uuid(for: CGMainDisplayID())
        return screens.first { $0.id == main }?.id ?? screens.first?.id
    }

    private static func currentScreens() -> [Screen] {
        var nameCounts: [String: Int] = [:]
        return NSScreen.screens.compactMap { screen in
            guard let number = screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber
            else { return nil }
            let displayID = CGDirectDisplayID(number.uint32Value)
            var name = screen.localizedName
            nameCounts[name, default: 0] += 1
            if let count = nameCounts[name], count > 1 { name += " \(count)" }
            return Screen(id: uuid(for: displayID), displayID: displayID, name: name,
                          isBuiltIn: CGDisplayIsBuiltin(displayID) != 0)
        }
    }

    private static func nsScreen(for displayID: CGDirectDisplayID) -> NSScreen? {
        NSScreen.screens.first {
            ($0.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber)?.uint32Value == displayID
        }
    }

    private static func uuid(for displayID: CGDirectDisplayID) -> String {
        guard let uuid = CGDisplayCreateUUIDFromDisplayID(displayID)?.takeRetainedValue(),
              let string = CFUUIDCreateString(nil, uuid) else { return "display-\(displayID)" }
        return string as String
    }

    // MARK: - Drift correction

    // macOS resets gamma tables after Night Shift, True Tone, screen saver and wake.
    private func observeSystem() {
        NotificationCenter.default
            .publisher(for: NSApplication.didChangeScreenParametersNotification)
            .debounce(for: .milliseconds(500), scheduler: RunLoop.main)
            .sink { [weak self] _ in self?.screensChanged() }
            .store(in: &cancellables)

        let workspace = NSWorkspace.shared.notificationCenter
        Publishers.Merge(
            workspace.publisher(for: NSWorkspace.screensDidWakeNotification),
            workspace.publisher(for: NSWorkspace.didWakeNotification)
        )
        .debounce(for: .milliseconds(500), scheduler: RunLoop.main)
        .sink { [weak self] _ in
            guard let self, !self.deskrivIsDimming else { return }
            self.gamma.reapplyAll()
        }
        .store(in: &cancellables)
    }

    private func startDriftTimer() {
        let timer = Timer(timeInterval: 2.0, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self, !self.deskrivIsDimming, self.gamma.hasAppliedTables else { return }
                // Skip while the user is away (KVM switched to another machine); the
                // activity monitor re-applies the moment they come back.
                guard Date().timeIntervalSince(self.lastUserActivity) < Self.awayInterval else { return }
                self.gamma.reapplyDrifted()
            }
        }
        timer.tolerance = 0.5
        RunLoop.main.add(timer, forMode: .common)
        driftTimer = timer
    }

    // When a KVM switches away, the Mac stops receiving HID events. The first event
    // after that means the user is back, so check the tables immediately.
    private func observeUserActivity() {
        let mask: NSEvent.EventTypeMask = [.mouseMoved, .leftMouseDown, .rightMouseDown, .leftMouseDragged, .scrollWheel]
        let global = NSEvent.addGlobalMonitorForEvents(matching: mask) { [weak self] _ in
            MainActor.assumeIsolated { self?.noteUserActivity() }
        }
        // Global monitors never see events sent to this app, e.g. dragging a slider.
        let local = NSEvent.addLocalMonitorForEvents(matching: mask) { [weak self] event in
            MainActor.assumeIsolated { self?.noteUserActivity() }
            return event
        }
        eventMonitors = [global, local].compactMap { $0 }
    }

    private func noteUserActivity() {
        let now = Date()
        let wasAway = now.timeIntervalSince(lastUserActivity) > Self.awayInterval
        lastUserActivity = now
        if wasAway && !deskrivIsDimming {
            gamma.reapplyDrifted()
        }
    }

    // MARK: - DeskRiv KVM integration

    // DeskRiv posts these from its macOS display_dim module.
    // displayDimmed:   DeskRiv applied CGSetDisplayTransferByFormula; hands off.
    // displayRestored: DeskRiv called CGDisplayRestoreColorSyncSettings; re-apply now.
    private func observeDeskRiv() {
        darwinObservers = [
            _DarwinObserver(name: "com.deskriv.displayDimmed") { [weak self] in
                Task { @MainActor [weak self] in self?.deskrivIsDimming = true }
            },
            _DarwinObserver(name: "com.deskriv.displayRestored") { [weak self] in
                Task { @MainActor [weak self] in
                    guard let self else { return }
                    self.deskrivIsDimming = false
                    self.screens.forEach { self.applyTone(to: $0) }
                }
            },
        ]
    }
}
