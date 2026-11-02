import Foundation

/// Persists per-display settings as one JSON blob keyed by display UUID.
struct SettingsStore {
    let defaults: UserDefaults

    private static let settingsKey = "displaySettings.v2"
    private static let selectionKey = "selectedScreen"
    private static let enabledKey = "enabled"

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
    }

    func load() -> [String: DisplaySettings] {
        guard let data = defaults.data(forKey: Self.settingsKey),
              let all = try? JSONDecoder().decode([String: DisplaySettings].self, from: data)
        else { return [:] }
        return all.mapValues { $0.clamped() }
    }

    func save(_ all: [String: DisplaySettings]) {
        guard let data = try? JSONEncoder().encode(all) else { return }
        defaults.set(data, forKey: Self.settingsKey)
    }

    var selectedScreenID: String? {
        get { defaults.string(forKey: Self.selectionKey) }
        nonmutating set { defaults.set(newValue, forKey: Self.selectionKey) }
    }

    var isEnabled: Bool {
        get { defaults.object(forKey: Self.enabledKey) as? Bool ?? true }
        nonmutating set { defaults.set(newValue, forKey: Self.enabledKey) }
    }

    /// Versions before per-display settings stored one flat key per value, all for
    /// the main display. The old keys are left in place so an older build sharing
    /// this bundle ID keeps its settings; the caller migrates only while the new
    /// store is empty.
    func legacySettings() -> DisplaySettings? {
        let keys = DisplaySettings.CodingKeys.allCases.map(\.rawValue)
        var found: [String: Double] = [:]
        for key in keys {
            if let value = defaults.object(forKey: key) as? Double {
                found[key] = value
            }
        }
        guard !found.isEmpty,
              let data = try? JSONSerialization.data(withJSONObject: found),
              let settings = try? JSONDecoder().decode(DisplaySettings.self, from: data)
        else { return nil }
        return settings.clamped()
    }
}
