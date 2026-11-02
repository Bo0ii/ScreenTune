using ScreenTune.Core;

namespace ScreenTune.Core.Tests;

public class GammaRampTests
{
    [Fact]
    public void RampHasThreeChannelsOf256()
    {
        var ramp = GammaRamp.FromSettings(new DisplaySettings());
        Assert.Equal(768, ramp.Length);
    }

    [Fact]
    public void DefaultSettingsGiveTheIdentityRamp()
    {
        var ramp = GammaRamp.FromSettings(new DisplaySettings());
        Assert.Equal(GammaRamp.Identity(), ramp);
        Assert.Equal(0, ramp[0]);
        Assert.Equal(65535, ramp[255]);
        Assert.Equal(65535, ramp[767]);
    }

    [Fact]
    public void ChannelsAreLaidOutRedGreenBlue()
    {
        var warm = GammaRamp.FromSettings(new DisplaySettings() with { Temperature = 3000 });
        Assert.Equal(65535, warm[255]);
        Assert.InRange(warm[511], 45000, 46500);   // green near 0.697
        Assert.InRange(warm[767], 28000, 29500);   // blue near 0.440
    }

    [Fact]
    public void MatchesToleratesDriverRounding()
    {
        var identity = GammaRamp.Identity();
        // Many drivers report a linear ramp as i << 8 instead of i * 257.
        var shifted = new ushort[768];
        for (var i = 0; i < 768; i++) shifted[i] = (ushort)((i % 256) << 8);
        Assert.True(GammaRamp.Matches(identity, shifted));
    }

    [Fact]
    public void MatchesDetectsDrift()
    {
        var applied = GammaRamp.FromSettings(new DisplaySettings() with { Temperature = 3000 });
        Assert.False(GammaRamp.Matches(applied, GammaRamp.Identity()));
        Assert.True(GammaRamp.Matches(applied, (ushort[])applied.Clone()));
    }

    [Fact]
    public void MatchesRejectsWrongLength()
    {
        Assert.False(GammaRamp.Matches(GammaRamp.Identity(), new ushort[10]));
    }
}
