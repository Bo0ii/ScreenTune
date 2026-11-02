using System.Text.Json.Serialization;

namespace ScreenTune.Core;

public enum Channel { Red, Green, Blue }

public enum SettingKey
{
    BlackRed, BlackGreen, BlackBlue,
    MidRed, MidGreen, MidBlue,
    WhiteRed, WhiteGreen, WhiteBlue,
    Gamma, Saturation, Contrast,
    Temperature,
}

public readonly record struct ValueRange(double Min, double Max)
{
    public double Span => Max - Min;
    public double Clamp(double value) => Math.Min(Math.Max(value, Min), Max);
}

public sealed record TransferTable(double[] Red, double[] Green, double[] Blue);

/// <summary>
/// Calibration values for one display. Same 13 values, ranges and math as the
/// macOS app, so profile files move between the two.
/// Setters rather than init: source-generated JSON assigns every init-only property,
/// which would turn a missing key into 0 instead of its default.
/// </summary>
public sealed record DisplaySettings
{
    public double BlackRed { get; set; }
    public double BlackGreen { get; set; }
    public double BlackBlue { get; set; }
    public double MidRed { get; set; } = 1.0;
    public double MidGreen { get; set; } = 1.0;
    public double MidBlue { get; set; } = 1.0;
    public double WhiteRed { get; set; } = 1.0;
    public double WhiteGreen { get; set; } = 1.0;
    public double WhiteBlue { get; set; } = 1.0;
    public double Gamma { get; set; } = 2.2;
    public double Saturation { get; set; } = 1.0;
    public double Contrast { get; set; } = 1.0;

    /// <summary>White point in Kelvin. 6500 is neutral; lower is warmer.</summary>
    public double Temperature { get; set; } = 6500.0;

    // A fresh instance each time: the type is mutable, so a shared default could be changed by accident.
    public static DisplaySettings Default => new();

    public static readonly ValueRange BlackRange = new(0.0, 0.5);
    public static readonly ValueRange MidRange = new(0.5, 1.5);
    public static readonly ValueRange WhiteRange = new(0.5, 1.5);
    public static readonly ValueRange GammaRange = new(0.5, 3.5);
    public static readonly ValueRange ContrastRange = new(0.0, 2.0);
    public static readonly ValueRange SaturationRange = new(0.0, 2.0);
    public static readonly ValueRange TemperatureRange = new(3000, 6500);

    /// <summary>Below this distance from 1.0 the saturation effect is not worth applying.</summary>
    public const double SaturationThreshold = 0.004;

    public static IReadOnlyList<SettingKey> AllKeys { get; } = Enum.GetValues<SettingKey>();

    // Identity

    [JsonIgnore]
    public bool IsToneIdentity => HasSameTone(Default);

    [JsonIgnore]
    public bool IsSaturationIdentity => Math.Abs(Saturation - 1.0) < SaturationThreshold;

    public bool HasSameTone(DisplaySettings other) =>
        (this with { Saturation = 1 }) == (other with { Saturation = 1 });

    // Keyed access

    public static ValueRange RangeOf(SettingKey key) => key switch
    {
        SettingKey.BlackRed or SettingKey.BlackGreen or SettingKey.BlackBlue => BlackRange,
        SettingKey.MidRed or SettingKey.MidGreen or SettingKey.MidBlue => MidRange,
        SettingKey.WhiteRed or SettingKey.WhiteGreen or SettingKey.WhiteBlue => WhiteRange,
        SettingKey.Gamma => GammaRange,
        SettingKey.Saturation => SaturationRange,
        SettingKey.Contrast => ContrastRange,
        SettingKey.Temperature => TemperatureRange,
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    public double Get(SettingKey key) => key switch
    {
        SettingKey.BlackRed => BlackRed,
        SettingKey.BlackGreen => BlackGreen,
        SettingKey.BlackBlue => BlackBlue,
        SettingKey.MidRed => MidRed,
        SettingKey.MidGreen => MidGreen,
        SettingKey.MidBlue => MidBlue,
        SettingKey.WhiteRed => WhiteRed,
        SettingKey.WhiteGreen => WhiteGreen,
        SettingKey.WhiteBlue => WhiteBlue,
        SettingKey.Gamma => Gamma,
        SettingKey.Saturation => Saturation,
        SettingKey.Contrast => Contrast,
        SettingKey.Temperature => Temperature,
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    public DisplaySettings With(SettingKey key, double value) => key switch
    {
        SettingKey.BlackRed => this with { BlackRed = value },
        SettingKey.BlackGreen => this with { BlackGreen = value },
        SettingKey.BlackBlue => this with { BlackBlue = value },
        SettingKey.MidRed => this with { MidRed = value },
        SettingKey.MidGreen => this with { MidGreen = value },
        SettingKey.MidBlue => this with { MidBlue = value },
        SettingKey.WhiteRed => this with { WhiteRed = value },
        SettingKey.WhiteGreen => this with { WhiteGreen = value },
        SettingKey.WhiteBlue => this with { WhiteBlue = value },
        SettingKey.Gamma => this with { Gamma = value },
        SettingKey.Saturation => this with { Saturation = value },
        SettingKey.Contrast => this with { Contrast = value },
        SettingKey.Temperature => this with { Temperature = value },
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    // Clamping

    /// <summary>
    /// Values from disk or an imported file can be anything. An out-of-range gamma or
    /// mid value can turn the whole screen black or white, so everything is forced
    /// back into the slider ranges and non-finite values fall back to defaults.
    /// </summary>
    public DisplaySettings Clamped()
    {
        var c = this;
        foreach (var key in AllKeys)
        {
            var v = c.Get(key);
            c = c.With(key, double.IsFinite(v) ? RangeOf(key).Clamp(v) : Default.Get(key));
        }
        return c;
    }

    // Transfer table

    /// <summary>
    /// Output level for input t (0...1): linearize with 2.2, contrast around 0.5,
    /// black lift / mid power / white scale and warmth, then re-encode with 1/gamma.
    /// </summary>
    public double Output(double t, Channel channel)
    {
        var warmth = WarmthMultipliers(Temperature);
        var (black, mid, white, warm) = channel switch
        {
            Channel.Red => (BlackRed, MidRed, WhiteRed, warmth.Red),
            Channel.Green => (BlackGreen, MidGreen, WhiteGreen, warmth.Green),
            _ => (BlackBlue, MidBlue, WhiteBlue, warmth.Blue),
        };

        var v = Math.Pow(Math.Max(t, 0), 2.2);
        if (Math.Abs(Contrast - 1.0) > 0.001)
        {
            v = Clamp01(0.5 + (v - 0.5) * Contrast);
        }
        v = black + v * (1.0 - black);
        v = Math.Pow(Math.Max(v, 0), 1.0 / mid);
        v = Clamp01(v * white * warm);
        v = Math.Pow(Math.Max(v, 0), 1.0 / Gamma);
        return Clamp01(v);
    }

    public TransferTable TransferTable(int size = 256)
    {
        var r = new double[size];
        var g = new double[size];
        var b = new double[size];
        var last = (double)Math.Max(size - 1, 1);
        for (var i = 0; i < size; i++)
        {
            var t = i / last;
            r[i] = Output(t, Channel.Red);
            g[i] = Output(t, Channel.Green);
            b[i] = Output(t, Channel.Blue);
        }
        return new TransferTable(r, g, b);
    }

    private static double Clamp01(double v) => Math.Min(Math.Max(v, 0), 1);

    // Warmth

    /// <summary>
    /// Linear-light channel multipliers that turn white into the color of a kelvin
    /// light source, normalized so 6500K is exactly (1, 1, 1).
    /// </summary>
    public static (double Red, double Green, double Blue) WarmthMultipliers(double kelvin)
    {
        if (!(kelvin < TemperatureRange.Max)) return (1, 1, 1);
        var target = BlackbodyRgb(kelvin);
        var neutral = BlackbodyRgb(TemperatureRange.Max);
        static double Linear(double a, double b) => Math.Pow(Math.Min(a / b, 1), 2.2);
        return (Linear(target.R, neutral.R), Linear(target.G, neutral.G), Linear(target.B, neutral.B));
    }

    // Tanner Helland's blackbody approximation (sRGB-encoded, 0...255), valid 1000K to 40000K.
    private static (double R, double G, double B) BlackbodyRgb(double kelvin)
    {
        var t = kelvin / 100;
        var r = t <= 66 ? 255 : 329.698727446 * Math.Pow(t - 60, -0.1332047592);
        var g = t <= 66
            ? 99.4708025861 * Math.Log(t) - 161.1195681661
            : 288.1221695283 * Math.Pow(t - 60, -0.0755148492);
        var b = t >= 66 ? 255 : (t <= 19 ? 0 : 138.5177312231 * Math.Log(t - 10) - 305.0447927307);
        static double C(double v) => Math.Min(Math.Max(v, 0), 255);
        return (C(r), C(g), C(b));
    }
}
