namespace ScreenTune.Core;

/// <summary>
/// The 768-entry WORD ramp SetDeviceGammaRamp takes: 256 red, then 256 green, then 256 blue.
/// </summary>
public static class GammaRamp
{
    public const int Size = 256;
    public const int Length = Size * 3;

    /// <summary>How far a read-back ramp may differ before it counts as reset by Windows or the driver.</summary>
    public const double DriftTolerance = 0.02;

    public static ushort[] FromSettings(DisplaySettings settings) => FromTable(settings.TransferTable(Size));

    public static ushort[] FromTable(TransferTable table)
    {
        var ramp = new ushort[Length];
        for (var i = 0; i < Size; i++)
        {
            ramp[i] = ToWord(table.Red[i]);
            ramp[Size + i] = ToWord(table.Green[i]);
            ramp[2 * Size + i] = ToWord(table.Blue[i]);
        }
        return ramp;
    }

    public static ushort[] Identity() => FromSettings(DisplaySettings.Default);

    public static bool Matches(ReadOnlySpan<ushort> a, ReadOnlySpan<ushort> b, double tolerance = DriftTolerance)
    {
        if (a.Length != Length || b.Length != Length) return false;
        var limit = tolerance * ushort.MaxValue;
        for (var i = 0; i < Length; i++)
        {
            if (Math.Abs(a[i] - b[i]) > limit) return false;
        }
        return true;
    }

    private static ushort ToWord(double v) => (ushort)Math.Round(Math.Clamp(v, 0, 1) * ushort.MaxValue);
}
