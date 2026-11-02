using System.Text.Json;
using ScreenTune.Core;

namespace ScreenTune.Core.Tests;

// Ported from the macOS app's Tests/main.swift so both apps share the same math.
public class DisplaySettingsTests
{
    private static readonly string[] LegacyKeys =
    [
        "blackRed", "blackGreen", "blackBlue",
        "midRed", "midGreen", "midBlue",
        "whiteRed", "whiteGreen", "whiteBlue",
        "gamma", "saturation", "contrast",
    ];

    // Identity

    [Fact]
    public void DefaultsAreIdentity()
    {
        var s = new DisplaySettings();
        Assert.True(s.IsToneIdentity);
        Assert.True(s.IsSaturationIdentity);
    }

    [Fact]
    public void DefaultTableIsAStraightLine()
    {
        var table = new DisplaySettings().TransferTable(256);
        Assert.Equal(256, table.Red.Length);
        Assert.Equal(256, table.Green.Length);
        Assert.Equal(256, table.Blue.Length);
        for (var i = 0; i < 256; i++)
        {
            Assert.Equal(i / 255.0, table.Red[i], 1e-4);
        }
    }

    [Fact]
    public void ChangingGammaIsNotIdentity()
    {
        Assert.False((new DisplaySettings() with { Gamma = 2.0 }).IsToneIdentity);
    }

    [Fact]
    public void SaturationAloneLeavesToneAtIdentity()
    {
        var sat = new DisplaySettings() with { Saturation = 0.5 };
        Assert.True(sat.IsToneIdentity);
        Assert.False(sat.IsSaturationIdentity);
    }

    [Fact]
    public void TinySaturationChangeCountsAsNone()
    {
        Assert.True((new DisplaySettings() with { Saturation = 1.002 }).IsSaturationIdentity);
    }

    [Fact]
    public void HasSameToneIgnoresSaturation()
    {
        var a = new DisplaySettings();
        var b = a with { Saturation = 0.3 };
        Assert.True(a.HasSameTone(b));
        Assert.False(a.HasSameTone(b with { MidRed = 1.1 }));
    }

    // Math

    [Fact]
    public void MidRedMatchesOriginalMath()
    {
        var s = new DisplaySettings() with { MidRed = 1.2 };
        Assert.Equal(0.5612, s.Output(0.5, Channel.Red), 1e-3);
        Assert.Equal(0.5, s.Output(0.5, Channel.Green), 1e-4);
    }

    [Fact]
    public void ExtremeSettingsStayInRangeAndRise()
    {
        var s = new DisplaySettings() with
        {
            Contrast = 2, Gamma = 0.5, BlackRed = 0.5, MidRed = 0.5, WhiteRed = 1.5,
        };
        var table = s.TransferTable(256);
        Assert.All(table.Red.Concat(table.Green).Concat(table.Blue), v => Assert.InRange(v, 0.0, 1.0));
        for (var i = 1; i < table.Red.Length; i++)
        {
            Assert.True(table.Red[i - 1] <= table.Red[i], $"red falls at {i}");
        }
    }

    // Warmth

    [Fact]
    public void NeutralTemperatureLeavesWhiteUntouched()
    {
        var neutral = new DisplaySettings();
        Assert.Equal(6500, neutral.Temperature);
        Assert.Equal(1.0, neutral.Output(1.0, Channel.Blue), 1e-6);
    }

    [Fact]
    public void WarmthAt3000KMatchesBlackbody()
    {
        var warm = new DisplaySettings() with { Temperature = 3000 };
        Assert.False(warm.IsToneIdentity);
        Assert.Equal(1.0, warm.Output(1.0, Channel.Red), 1e-6);
        Assert.Equal(0.697, warm.Output(1.0, Channel.Green), 5e-3);
        Assert.Equal(0.440, warm.Output(1.0, Channel.Blue), 5e-3);
        Assert.Equal(0.0, warm.Output(0.0, Channel.Blue), 1e-6);
    }

    [Fact]
    public void WarmthIsOrdered()
    {
        var warm = new DisplaySettings() with { Temperature = 3000 };
        var less = new DisplaySettings() with { Temperature = 5000 };
        Assert.True(less.Output(1.0, Channel.Blue) > warm.Output(1.0, Channel.Blue));
        Assert.True(less.Output(1.0, Channel.Blue) < 1.0);
    }

    // Clamping

    [Fact]
    public void TemperatureClamps()
    {
        var s = new DisplaySettings();
        Assert.Equal(DisplaySettings.TemperatureRange.Min, (s with { Temperature = 1000 }).Clamped().Temperature);
        Assert.Equal(DisplaySettings.TemperatureRange.Max, (s with { Temperature = 9000 }).Clamped().Temperature);
        Assert.Equal(6500, (s with { Temperature = double.NaN }).Clamped().Temperature);
    }

    [Fact]
    public void EverythingClampsOrFallsBack()
    {
        var s = new DisplaySettings() with
        {
            Gamma = 0,
            Saturation = 5,
            BlackRed = -1,
            MidGreen = double.NaN,
            WhiteBlue = double.PositiveInfinity,
            Contrast = 9,
        };
        var c = s.Clamped();
        Assert.Equal(DisplaySettings.GammaRange.Min, c.Gamma);
        Assert.Equal(DisplaySettings.SaturationRange.Max, c.Saturation);
        Assert.Equal(0, c.BlackRed);
        Assert.Equal(1, c.MidGreen);
        Assert.Equal(1, c.WhiteBlue);
        Assert.Equal(DisplaySettings.ContrastRange.Max, c.Contrast);
        Assert.Equal(new DisplaySettings(), new DisplaySettings().Clamped());
    }

    // Keyed access (sliders bind by key)

    [Fact]
    public void KeyedGetAndWithRoundTrip()
    {
        var s = new DisplaySettings();
        foreach (var key in DisplaySettings.AllKeys)
        {
            var range = DisplaySettings.RangeOf(key);
            var value = range.Min + range.Span * 0.25;
            var changed = s.With(key, value);
            Assert.Equal(value, changed.Get(key));
            foreach (var other in DisplaySettings.AllKeys.Where(k => k != key))
            {
                Assert.Equal(s.Get(other), changed.Get(other));
            }
        }
        Assert.Equal(13, DisplaySettings.AllKeys.Count);
    }

    // JSON (profile files are shared with the macOS app)

    [Fact]
    public void OldMacProfileDecodes()
    {
        const string json = """
            {"blackBlue":0,"blackGreen":0,"blackRed":0.1,"contrast":1,"gamma":2.4,"midBlue":1,"midGreen":1,"midRed":1,"saturation":0.8,"whiteBlue":1,"whiteGreen":1,"whiteRed":0.9}
            """;
        var s = Profiles.Decode(json);
        Assert.Equal(0.1, s.BlackRed);
        Assert.Equal(2.4, s.Gamma);
        Assert.Equal(0.8, s.Saturation);
        Assert.Equal(0.9, s.WhiteRed);
        Assert.Equal(6500, s.Temperature);
    }

    [Fact]
    public void MissingKeysFallBackToDefaults()
    {
        var partial = Profiles.Decode("""{"gamma":2.0}""");
        Assert.Equal(2.0, partial.Gamma);
        Assert.Equal(1.0, partial.MidRed);
    }

    [Fact]
    public void ImportedValuesAreClamped()
    {
        Assert.Equal(DisplaySettings.GammaRange.Max, Profiles.Decode("""{"gamma":99}""").Gamma);
    }

    [Fact]
    public void ExportUsesTheMacKeys()
    {
        var json = Profiles.Encode(new DisplaySettings() with { Gamma = 2.4 });
        using var doc = JsonDocument.Parse(json);
        var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();
        Assert.Equal(LegacyKeys.Append("temperature").ToHashSet(), keys);
        Assert.Equal(2.4, doc.RootElement.GetProperty("gamma").GetDouble());
    }

    [Fact]
    public void InvalidProfileThrows()
    {
        Assert.ThrowsAny<JsonException>(() => Profiles.Decode("not json"));
        Assert.ThrowsAny<JsonException>(() => Profiles.Decode("[1,2]"));
    }
}
