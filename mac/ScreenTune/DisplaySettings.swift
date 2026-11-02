import Foundation

/// Calibration values for one display. Pure value type with no AppKit/CoreGraphics
/// dependency so it can be checked by Tests/run.sh.
struct DisplaySettings: Codable, Equatable {
    var blackRed = 0.0, blackGreen = 0.0, blackBlue = 0.0
    var midRed = 1.0, midGreen = 1.0, midBlue = 1.0
    var whiteRed = 1.0, whiteGreen = 1.0, whiteBlue = 1.0
    var gamma = 2.2
    var saturation = 1.0
    var contrast = 1.0
    /// White point in Kelvin. 6500 is neutral; lower is warmer.
    var temperature = 6500.0

    static let blackRange: ClosedRange<Double> = 0.0...0.5
    static let midRange: ClosedRange<Double> = 0.5...1.5
    static let whiteRange: ClosedRange<Double> = 0.5...1.5
    static let gammaRange: ClosedRange<Double> = 0.5...3.5
    static let contrastRange: ClosedRange<Double> = 0.0...2.0
    static let saturationRange: ClosedRange<Double> = 0.0...2.0
    static let temperatureRange: ClosedRange<Double> = 3000...6500

    /// Below this distance from 1.0 the saturation overlay is not worth its GPU cost.
    static let saturationThreshold = 0.004

    enum Channel { case red, green, blue }

    init() {}

    // MARK: - Identity

    var isToneIdentity: Bool {
        hasSameTone(as: DisplaySettings())
    }

    var isSaturationIdentity: Bool {
        abs(saturation - 1.0) < Self.saturationThreshold
    }

    func hasSameTone(as other: DisplaySettings) -> Bool {
        var a = self, b = other
        a.saturation = 1
        b.saturation = 1
        return a == b
    }

    // MARK: - Clamping

    /// Values from disk or an imported file can be anything. Out-of-range gamma or
    /// mid values can turn the whole screen black or white, so everything is forced
    /// back into the slider ranges, and non-finite values fall back to defaults.
    func clamped() -> DisplaySettings {
        let d = DisplaySettings()
        func fix(_ v: Double, _ range: ClosedRange<Double>, _ fallback: Double) -> Double {
            guard v.isFinite else { return fallback }
            return min(max(v, range.lowerBound), range.upperBound)
        }
        var c = self
        c.blackRed = fix(blackRed, Self.blackRange, d.blackRed)
        c.blackGreen = fix(blackGreen, Self.blackRange, d.blackGreen)
        c.blackBlue = fix(blackBlue, Self.blackRange, d.blackBlue)
        c.midRed = fix(midRed, Self.midRange, d.midRed)
        c.midGreen = fix(midGreen, Self.midRange, d.midGreen)
        c.midBlue = fix(midBlue, Self.midRange, d.midBlue)
        c.whiteRed = fix(whiteRed, Self.whiteRange, d.whiteRed)
        c.whiteGreen = fix(whiteGreen, Self.whiteRange, d.whiteGreen)
        c.whiteBlue = fix(whiteBlue, Self.whiteRange, d.whiteBlue)
        c.gamma = fix(gamma, Self.gammaRange, d.gamma)
        c.saturation = fix(saturation, Self.saturationRange, d.saturation)
        c.contrast = fix(contrast, Self.contrastRange, d.contrast)
        c.temperature = fix(temperature, Self.temperatureRange, d.temperature)
        return c
    }

    // MARK: - Transfer table

    /// Output level for input `t` (0...1): linearize with 2.2, contrast around 0.5,
    /// black lift / mid power / white scale and warmth, then re-encode with 1/gamma.
    func output(_ t: Double, channel: Channel) -> Double {
        let (black, mid, white): (Double, Double, Double)
        let warmth = Self.warmthMultipliers(kelvin: temperature)
        let warm: Double
        switch channel {
        case .red: (black, mid, white, warm) = (blackRed, midRed, whiteRed, warmth.red)
        case .green: (black, mid, white, warm) = (blackGreen, midGreen, whiteGreen, warmth.green)
        case .blue: (black, mid, white, warm) = (blackBlue, midBlue, whiteBlue, warmth.blue)
        }
        var v = pow(max(t, 0), 2.2)
        if abs(contrast - 1.0) > 0.001 {
            v = Self.clamp(0.5 + (v - 0.5) * contrast)
        }
        v = black + v * (1.0 - black)
        v = pow(max(v, 0), 1.0 / mid)
        v = Self.clamp(v * white * warm)
        v = pow(max(v, 0), 1.0 / gamma)
        return Self.clamp(v)
    }

    func transferTable(size: Int = 256) -> (red: [Float], green: [Float], blue: [Float]) {
        var r = [Float](repeating: 0, count: size)
        var g = [Float](repeating: 0, count: size)
        var b = [Float](repeating: 0, count: size)
        let last = Double(max(size - 1, 1))
        for i in 0..<size {
            let t = Double(i) / last
            r[i] = Float(output(t, channel: .red))
            g[i] = Float(output(t, channel: .green))
            b[i] = Float(output(t, channel: .blue))
        }
        return (r, g, b)
    }

    private static func clamp(_ v: Double) -> Double { min(max(v, 0), 1) }

    // MARK: - Warmth

    /// Linear-light channel multipliers that turn white into the color of a
    /// `kelvin` light source, normalized so 6500K is exactly (1, 1, 1).
    static func warmthMultipliers(kelvin: Double) -> (red: Double, green: Double, blue: Double) {
        guard kelvin < temperatureRange.upperBound else { return (1, 1, 1) }
        let target = blackbodyRGB(kelvin: kelvin)
        let neutral = blackbodyRGB(kelvin: temperatureRange.upperBound)
        func linear(_ a: Double, _ b: Double) -> Double { pow(min(a / b, 1), 2.2) }
        return (linear(target.r, neutral.r), linear(target.g, neutral.g), linear(target.b, neutral.b))
    }

    // Tanner Helland's blackbody approximation (sRGB-encoded, 0...255), valid 1000K to 40000K.
    private static func blackbodyRGB(kelvin: Double) -> (r: Double, g: Double, b: Double) {
        let t = kelvin / 100
        let r = t <= 66 ? 255 : 329.698727446 * pow(t - 60, -0.1332047592)
        let g = t <= 66
            ? 99.4708025861 * log(t) - 161.1195681661
            : 288.1221695283 * pow(t - 60, -0.0755148492)
        let b = t >= 66 ? 255 : (t <= 19 ? 0 : 138.5177312231 * log(t - 10) - 305.0447927307)
        return (min(max(r, 0), 255), min(max(g, 0), 255), min(max(b, 0), 255))
    }

    // MARK: - Codable

    // Keys match the original CalibrationProfile JSON, so old exported files import.
    enum CodingKeys: String, CodingKey, CaseIterable {
        case blackRed, blackGreen, blackBlue
        case midRed, midGreen, midBlue
        case whiteRed, whiteGreen, whiteBlue
        case gamma, saturation, contrast
        case temperature
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let d = DisplaySettings()
        func read(_ key: CodingKeys, _ fallback: Double) throws -> Double {
            try c.decodeIfPresent(Double.self, forKey: key) ?? fallback
        }
        blackRed = try read(.blackRed, d.blackRed)
        blackGreen = try read(.blackGreen, d.blackGreen)
        blackBlue = try read(.blackBlue, d.blackBlue)
        midRed = try read(.midRed, d.midRed)
        midGreen = try read(.midGreen, d.midGreen)
        midBlue = try read(.midBlue, d.midBlue)
        whiteRed = try read(.whiteRed, d.whiteRed)
        whiteGreen = try read(.whiteGreen, d.whiteGreen)
        whiteBlue = try read(.whiteBlue, d.whiteBlue)
        gamma = try read(.gamma, d.gamma)
        saturation = try read(.saturation, d.saturation)
        contrast = try read(.contrast, d.contrast)
        temperature = try read(.temperature, d.temperature)
    }
}
