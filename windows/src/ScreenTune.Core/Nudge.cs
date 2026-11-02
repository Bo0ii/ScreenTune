namespace ScreenTune.Core;

/// <summary>
/// Fine steps for the − / + buttons, arrow keys, mouse wheel and slider drag.
/// Every value lives on a grid of its step, so the number on screen is the exact value.
/// </summary>
public static class Nudge
{
    /// <summary>
    /// One step per setting. Many single steps are below what an 8-bit screen can show;
    /// they are there so a value can be set exactly, and a few together become visible.
    /// </summary>
    public static double Step(SettingKey key) => key == SettingKey.Temperature ? 1 : 0.001;

    /// <summary>Decimal places needed to show one step.</summary>
    public static int Decimals(double step) =>
        Math.Max(0, (int)Math.Ceiling(-Math.Log10(step) - 1e-9));

    /// <summary>
    /// Moves <paramref name="value"/> by <paramref name="steps"/> steps, clamped to the range.
    /// An off-grid value first rounds to the nearest grid line, the same way it is shown,
    /// so a nudge always moves one step from the number on screen.
    /// </summary>
    public static double Apply(double value, double step, int steps, ValueRange range)
    {
        var line = Math.Round(value / step, MidpointRounding.AwayFromZero) + steps;
        return range.Clamp(Math.Round(line * step, Decimals(step)));
    }
}
