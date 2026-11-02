using ScreenTune.Core;

namespace ScreenTune.Core.Tests;

public class NudgeTests
{
    [Theory]
    [InlineData(SettingKey.BlackRed, 0.001)]
    [InlineData(SettingKey.BlackBlue, 0.001)]
    [InlineData(SettingKey.MidGreen, 0.001)]
    [InlineData(SettingKey.WhiteRed, 0.001)]
    [InlineData(SettingKey.Gamma, 0.001)]
    [InlineData(SettingKey.Contrast, 0.001)]
    [InlineData(SettingKey.Saturation, 0.001)]
    [InlineData(SettingKey.Temperature, 1)]
    public void StepPerSetting(SettingKey key, double step)
    {
        Assert.Equal(step, Nudge.Step(key));
    }

    [Theory]
    [InlineData(0.001, 3)]
    [InlineData(0.01, 2)]
    [InlineData(10, 0)]
    [InlineData(1, 0)]
    public void DecimalsShowOneStep(double step, int decimals)
    {
        Assert.Equal(decimals, Nudge.Decimals(step));
    }

    [Fact]
    public void OneStepUpAndDown()
    {
        Assert.Equal(0.51, Nudge.Apply(0.50, 0.01, 1, DisplaySettings.MidRange));
        Assert.Equal(0.49, Nudge.Apply(0.50, 0.01, -1, DisplaySettings.ContrastRange));
        Assert.Equal(2.21, Nudge.Apply(2.2, 0.01, 1, DisplaySettings.GammaRange));
        Assert.Equal(0.001, Nudge.Apply(0, 0.001, 1, DisplaySettings.BlackRange));
        Assert.Equal(3010, Nudge.Apply(3000, 10, 1, DisplaySettings.TemperatureRange));
        Assert.Equal(2.221, Nudge.Apply(2.22, 0.001, 1, DisplaySettings.GammaRange));
        Assert.Equal(2.219, Nudge.Apply(2.2199999999999998, 0.001, -1, DisplaySettings.GammaRange));
        Assert.Equal(6499, Nudge.Apply(6500, 1, -1, DisplaySettings.TemperatureRange));
    }

    [Fact]
    public void SeveralStepsAtOnce()
    {
        Assert.Equal(0.55, Nudge.Apply(0.50, 0.01, 5, DisplaySettings.MidRange));
        Assert.Equal(2.17, Nudge.Apply(2.2, 0.01, -3, DisplaySettings.GammaRange));
    }

    // A value between grid lines (from an old drag or an imported profile) is shown
    // rounded, so a nudge moves one step from the number on screen.
    [Fact]
    public void OffGridValueStepsFromTheShownNumber()
    {
        Assert.Equal(1.24, Nudge.Apply(1.2345, 0.01, 1, DisplaySettings.MidRange));
        Assert.Equal(1.22, Nudge.Apply(1.2345, 0.01, -1, DisplaySettings.MidRange));
        Assert.Equal(1.24, Nudge.Apply(1.226, 0.01, 1, DisplaySettings.MidRange));
    }

    [Fact]
    public void StopsAtTheEnds()
    {
        Assert.Equal(1.5, Nudge.Apply(1.5, 0.01, 1, DisplaySettings.MidRange));
        Assert.Equal(1.5, Nudge.Apply(1.4995, 0.01, 1, DisplaySettings.MidRange));
        Assert.Equal(0, Nudge.Apply(0, 0.001, -1, DisplaySettings.BlackRange));
        Assert.Equal(6500, Nudge.Apply(6500, 10, 1, DisplaySettings.TemperatureRange));
    }

    // Float noise (0.07 is 0.07000000000000001 when built from 7 * 0.01) must never
    // pile up, skip a step or show up in saved profiles.
    [Fact]
    public void RepeatedNudgesLandOnExactValues()
    {
        var up = 0.5;
        for (var i = 0; i < 100; i++)
        {
            up = Nudge.Apply(up, 0.01, 1, DisplaySettings.MidRange);
            Assert.Equal(Math.Round(0.5 + (i + 1) * 0.01, 2), up);
        }
        Assert.Equal(1.5, up);

        var fine = 0.5;
        for (var i = 0; i < 1000; i++)
        {
            fine = Nudge.Apply(fine, 0.001, 1, DisplaySettings.MidRange);
            Assert.Equal(Math.Round(0.5 + (i + 1) * 0.001, 3), fine);
        }
        Assert.Equal(1.5, fine);

        var down = 0.5;
        for (var i = 0; i < 500; i++)
        {
            down = Nudge.Apply(down, 0.001, -1, DisplaySettings.BlackRange);
        }
        Assert.Equal(0, down);
    }

    [Fact]
    public void ZeroStepsSnapsToTheNearestLine()
    {
        Assert.Equal(0.12, Nudge.Apply(0.1235, 0.01, 0, DisplaySettings.BlackRange));
        Assert.Equal(0.124, Nudge.Apply(0.1235, 0.001, 0, DisplaySettings.BlackRange));
    }
}
