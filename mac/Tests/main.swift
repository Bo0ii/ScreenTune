// Logic checks for the pure (AppKit-free) parts of ScreenTune.
// Run with Tests/run.sh. There is no Xcode test target.
import Foundation

var passes = 0
var failures = 0

func check(_ condition: @autoclosure () -> Bool, _ message: String, line: Int = #line) {
    if condition() {
        passes += 1
    } else {
        failures += 1
        print("FAIL (line \(line)): \(message)")
    }
}

func approx(_ a: Double, _ b: Double, _ tolerance: Double = 1e-4) -> Bool {
    abs(a - b) <= tolerance
}

func freshDefaults() -> (UserDefaults, String) {
    let suite = "screentune.tests.\(UUID().uuidString)"
    return (UserDefaults(suiteName: suite)!, suite)
}

let legacyKeys: Set<String> = [
    "blackRed", "blackGreen", "blackBlue",
    "midRed", "midGreen", "midBlue",
    "whiteRed", "whiteGreen", "whiteBlue",
    "gamma", "saturation", "contrast",
]

// MARK: - Identity

do {
    let s = DisplaySettings()
    check(s.isToneIdentity, "default tone is identity")
    check(s.isSaturationIdentity, "default saturation is identity")

    let table = s.transferTable(size: 256)
    check(table.red.count == 256 && table.green.count == 256 && table.blue.count == 256, "table has 256 entries per channel")
    let linear = (0..<256).allSatisfy { approx(Double(table.red[$0]), Double($0) / 255, 1e-4) }
    check(linear, "default settings produce a straight-line table")
}

do {
    var gamma = DisplaySettings()
    gamma.gamma = 2.0
    check(!gamma.isToneIdentity, "changing gamma is not identity")

    var sat = DisplaySettings()
    sat.saturation = 0.5
    check(sat.isToneIdentity, "saturation alone leaves tone at identity")
    check(!sat.isSaturationIdentity, "saturation 0.5 is not identity")

    var tiny = DisplaySettings()
    tiny.saturation = 1.002
    check(tiny.isSaturationIdentity, "a change below the threshold counts as no saturation")
}

do {
    let a = DisplaySettings()
    var b = a
    b.saturation = 0.3
    check(a.hasSameTone(as: b), "saturation difference keeps the same tone")
    b.midRed = 1.1
    check(!a.hasSameTone(as: b), "mid red difference changes tone")
}

// MARK: - Math

do {
    // Same pipeline as the original app: decode 2.2, contrast, tone, encode 1/gamma.
    var s = DisplaySettings()
    s.midRed = 1.2
    check(approx(s.output(0.5, channel: .red), 0.5612, 1e-3), "mid red 1.2 at 50% matches original math")
    check(approx(s.output(0.5, channel: .green), 0.5, 1e-4), "untouched green stays linear")
}

do {
    var s = DisplaySettings()
    s.contrast = 2
    s.gamma = 0.5
    s.blackRed = 0.5
    s.midRed = 0.5
    s.whiteRed = 1.5
    let table = s.transferTable(size: 256)
    let inRange = (table.red + table.green + table.blue).allSatisfy { $0 >= 0 && $0 <= 1 }
    check(inRange, "extreme settings stay within 0...1")
    let rising = zip(table.red, table.red.dropFirst()).allSatisfy { $0 <= $1 }
    check(rising, "red table never goes down")
}

// MARK: - Warmth

do {
    let neutral = DisplaySettings()
    check(neutral.temperature == 6500, "default temperature is 6500K")
    check(approx(neutral.output(1.0, channel: .blue), 1.0, 1e-6), "6500K leaves white untouched")

    var warm = DisplaySettings()
    warm.temperature = 3000
    check(!warm.isToneIdentity, "warmth changes tone")
    check(approx(warm.output(1.0, channel: .red), 1.0, 1e-6), "warmth keeps red at full")
    check(approx(warm.output(1.0, channel: .green), 0.697, 5e-3), "3000K white has green near 0.697")
    check(approx(warm.output(1.0, channel: .blue), 0.440, 5e-3), "3000K white has blue near 0.440")
    check(approx(warm.output(0.0, channel: .blue), 0.0, 1e-6), "black stays black")

    var less = DisplaySettings()
    less.temperature = 5000
    check(less.output(1.0, channel: .blue) > warm.output(1.0, channel: .blue), "5000K is less warm than 3000K")
    check(less.output(1.0, channel: .blue) < 1.0, "5000K is warmer than neutral")
}

// MARK: - Clamping

do {
    var s = DisplaySettings()
    s.temperature = 1000
    check(s.clamped().temperature == DisplaySettings.temperatureRange.lowerBound, "temperature clamps to minimum")
    s.temperature = 9000
    check(s.clamped().temperature == DisplaySettings.temperatureRange.upperBound, "temperature clamps to maximum")
    s.temperature = .nan
    check(s.clamped().temperature == 6500, "NaN temperature falls back to neutral")
}

do {
    var s = DisplaySettings()
    s.gamma = 0
    s.saturation = 5
    s.blackRed = -1
    s.midGreen = .nan
    s.whiteBlue = .infinity
    s.contrast = 9
    let c = s.clamped()
    check(c.gamma == DisplaySettings.gammaRange.lowerBound, "gamma 0 clamps to minimum")
    check(c.saturation == DisplaySettings.saturationRange.upperBound, "saturation 5 clamps to maximum")
    check(c.blackRed == 0, "negative black clamps to 0")
    check(c.midGreen == 1, "NaN falls back to default")
    check(c.whiteBlue == 1, "infinity falls back to default")
    check(c.contrast == DisplaySettings.contrastRange.upperBound, "contrast 9 clamps to maximum")
    check(DisplaySettings().clamped() == DisplaySettings(), "defaults are unchanged by clamping")
}

// MARK: - JSON (profile files)

do {
    let json = """
    {"blackBlue":0,"blackGreen":0,"blackRed":0.1,"contrast":1,"gamma":2.4,"midBlue":1,"midGreen":1,"midRed":1,"saturation":0.8,"whiteBlue":1,"whiteGreen":1,"whiteRed":0.9}
    """
    let s = try JSONDecoder().decode(DisplaySettings.self, from: Data(json.utf8))
    check(s.blackRed == 0.1 && s.gamma == 2.4 && s.saturation == 0.8 && s.whiteRed == 0.9, "old profile file decodes")

    let partial = try JSONDecoder().decode(DisplaySettings.self, from: Data(#"{"gamma":2.0}"#.utf8))
    check(partial.gamma == 2.0 && partial.midRed == 1.0, "missing keys fall back to defaults")

    let data = try JSONEncoder().encode(s)
    let object = try JSONSerialization.jsonObject(with: data) as? [String: Double] ?? [:]
    check(Set(object.keys) == legacyKeys.union(["temperature"]), "exported keys are the old profile format plus temperature")
    check(s.temperature == 6500, "old profile without temperature gets neutral 6500K")
} catch {
    check(false, "JSON decoding threw: \(error)")
}

// MARK: - Store

do {
    let (defaults, suite) = freshDefaults()
    defer { defaults.removePersistentDomain(forName: suite) }
    let store = SettingsStore(defaults: defaults)

    check(store.load().isEmpty, "fresh store is empty")

    var a = DisplaySettings()
    a.gamma = 2.0
    store.save(["X": a])
    check(store.load()["X"] == a, "save then load round-trips")

    var bad = DisplaySettings()
    bad.gamma = 99
    store.save(["Y": bad])
    check(store.load()["Y"]?.gamma == DisplaySettings.gammaRange.upperBound, "bad stored values are clamped on load")

    check(store.selectedScreenID == nil, "no selection at first")
    store.selectedScreenID = "X"
    check(store.selectedScreenID == "X", "selection round-trips")

    check(store.isEnabled, "tuning is on by default")
    store.isEnabled = false
    check(!store.isEnabled, "off state round-trips")
}

do {
    let (defaults, suite) = freshDefaults()
    defer { defaults.removePersistentDomain(forName: suite) }
    let store = SettingsStore(defaults: defaults)

    check(store.legacySettings() == nil, "nothing to migrate when no old keys exist")

    defaults.set(0.2, forKey: "blackRed")
    defaults.set(1.8, forKey: "gamma")
    defaults.set(0.5, forKey: "saturation")
    let migrated = store.legacySettings()
    check(migrated?.blackRed == 0.2 && migrated?.gamma == 1.8 && migrated?.saturation == 0.5, "old flat keys migrate")
    check(migrated?.midRed == 1.0, "unset old keys use defaults")
    check(defaults.object(forKey: "gamma") != nil, "old keys are kept so older builds keep their settings")

    defaults.set(99.0, forKey: "gamma")
    check(store.legacySettings()?.gamma == DisplaySettings.gammaRange.upperBound, "old values are clamped")
}

// MARK: - Fine steps (same cases as windows/tests/ScreenTune.Core.Tests/NudgeTests.cs)

do {
    check(Nudge.fine == 0.001 && Nudge.warmth == 1, "one step is 0.001, warmth 1K")
    check(Nudge.decimals(0.001) == 3 && Nudge.decimals(0.01) == 2 && Nudge.decimals(10) == 0 && Nudge.decimals(1) == 0,
          "decimals show exactly one step")

    check(Nudge.apply(0.50, step: 0.01, steps: 1, range: DisplaySettings.midRange) == 0.51, "one step up")
    check(Nudge.apply(0.50, step: 0.01, steps: -1, range: DisplaySettings.contrastRange) == 0.49, "one step down")
    check(Nudge.apply(0, step: 0.001, steps: 1, range: DisplaySettings.blackRange) == 0.001, "black moves 0.001")
    check(Nudge.apply(3000, step: 10, steps: 1, range: DisplaySettings.temperatureRange) == 3010, "coarse step")
    check(Nudge.apply(2.22, step: 0.001, steps: 1, range: DisplaySettings.gammaRange) == 2.221, "gamma moves 0.001")
    check(Nudge.apply(2.2199999999999998, step: 0.001, steps: -1, range: DisplaySettings.gammaRange) == 2.219,
          "float noise on the stored value does not skip a step")
    check(Nudge.apply(6500, step: 1, steps: -1, range: DisplaySettings.temperatureRange) == 6499, "warmth moves 1K")
    check(Nudge.apply(0.50, step: 0.01, steps: 5, range: DisplaySettings.midRange) == 0.55, "several steps at once")

    // An off-grid value is shown rounded, so a nudge moves one step from the number on screen.
    check(Nudge.apply(1.2345, step: 0.01, steps: 1, range: DisplaySettings.midRange) == 1.24, "off-grid up")
    check(Nudge.apply(1.2345, step: 0.01, steps: -1, range: DisplaySettings.midRange) == 1.22, "off-grid down")
    check(Nudge.apply(0.1235, step: 0.001, steps: 0, range: DisplaySettings.blackRange) == 0.124, "zero steps snaps to the grid")

    check(Nudge.apply(1.5, step: 0.001, steps: 1, range: DisplaySettings.midRange) == 1.5, "stops at the top")
    check(Nudge.apply(0, step: 0.001, steps: -1, range: DisplaySettings.blackRange) == 0, "stops at the bottom")

    var fine = 0.5
    var exact = true
    for i in 1...1000 {
        fine = Nudge.apply(fine, step: 0.001, steps: 1, range: DisplaySettings.midRange)
        if fine != ((0.5 + Double(i) * 0.001) * 1000).rounded() / 1000 { exact = false }
    }
    check(exact && fine == 1.5, "1000 nudges land on exact values with no drift")
}

print("\(passes) passed, \(failures) failed")
exit(failures == 0 ? 0 : 1)
