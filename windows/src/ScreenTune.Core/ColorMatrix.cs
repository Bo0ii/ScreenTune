namespace ScreenTune.Core;

/// <summary>
/// 5x5 color matrices in MAGCOLOREFFECT layout: row-major, out = [r g b a 1] x M,
/// so row i is the input channel and column j the output channel.
/// </summary>
public static class ColorMatrix
{
    public const double LumaRed = 0.2126;
    public const double LumaGreen = 0.7152;
    public const double LumaBlue = 0.0722;

    public static float[] Identity { get; } = Saturation(1);

    /// <summary>0 = gray, 1 = unchanged, 2 = boosted. Grays stay gray at every amount.</summary>
    public static float[] Saturation(double amount)
    {
        double[] luma = [LumaRed, LumaGreen, LumaBlue];
        var m = new float[25];
        for (var i = 0; i < 3; i++)
        for (var j = 0; j < 3; j++)
        {
            m[i * 5 + j] = (float)(luma[i] * (1 - amount) + (i == j ? amount : 0));
        }
        m[3 * 5 + 3] = 1;
        m[4 * 5 + 4] = 1;
        return m;
    }
}
