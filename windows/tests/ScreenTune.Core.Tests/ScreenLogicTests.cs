using ScreenTune.Core;

namespace ScreenTune.Core.Tests;

public class ScreenNamingTests
{
    [Fact]
    public void DuplicateNamesAreNumbered()
    {
        Assert.Equal(["Dell U2720Q", "LG", "Dell U2720Q 2", "Dell U2720Q 3"],
            ScreenNaming.Disambiguate(["Dell U2720Q", "LG", "Dell U2720Q", "Dell U2720Q"]));
    }

    [Fact]
    public void UniqueNamesAreKept()
    {
        Assert.Equal(["A", "B"], ScreenNaming.Disambiguate(["A", "B"]));
    }

    [Fact]
    public void EmptyNamesGetAFallback()
    {
        Assert.Equal("Built-in Display", ScreenNaming.NameOrFallback("", isBuiltIn: true, index: 0));
        Assert.Equal("Display 2", ScreenNaming.NameOrFallback("  ", isBuiltIn: false, index: 1));
        Assert.Equal("LG ULTRAGEAR", ScreenNaming.NameOrFallback("LG ULTRAGEAR", isBuiltIn: false, index: 1));
    }
}

public class FlyoutPlacementTests
{
    private static readonly PixelRect Monitor = new(0, 0, 1920, 1080);
    private static readonly PixelRect BottomWork = new(0, 0, 1920, 1032);

    [Fact]
    public void DetectsEachTaskbarEdge()
    {
        var anchor = new PixelRect(1800, 1040, 1824, 1064);
        Assert.Equal(TaskbarEdge.Bottom, FlyoutPlacement.DetectEdge(Monitor, BottomWork, anchor));
        Assert.Equal(TaskbarEdge.Top, FlyoutPlacement.DetectEdge(Monitor, new PixelRect(0, 48, 1920, 1080), anchor));
        Assert.Equal(TaskbarEdge.Left, FlyoutPlacement.DetectEdge(Monitor, new PixelRect(60, 0, 1920, 1080), anchor));
        Assert.Equal(TaskbarEdge.Right, FlyoutPlacement.DetectEdge(Monitor, new PixelRect(0, 0, 1860, 1080), anchor));
    }

    [Fact]
    public void AutoHideTaskbarUsesTheNearestEdgeToTheIcon()
    {
        Assert.Equal(TaskbarEdge.Bottom, FlyoutPlacement.DetectEdge(Monitor, Monitor, new PixelRect(900, 1060, 920, 1080)));
        Assert.Equal(TaskbarEdge.Top, FlyoutPlacement.DetectEdge(Monitor, Monitor, new PixelRect(900, 0, 920, 20)));
    }

    [Fact]
    public void CentersOverTheIconAboveABottomTaskbar()
    {
        var anchor = new PixelRect(900, 1040, 920, 1064);
        var r = FlyoutPlacement.Place(anchor, BottomWork, TaskbarEdge.Bottom, 360, 700, 12);
        Assert.Equal(910 - 180, r.Left);
        Assert.Equal(1032 - 12 - 700, r.Top);
        Assert.Equal(360, r.Width);
        Assert.Equal(700, r.Height);
    }

    [Fact]
    public void ClampsToTheWorkAreaEdge()
    {
        var anchor = new PixelRect(1880, 1040, 1904, 1064);
        var r = FlyoutPlacement.Place(anchor, BottomWork, TaskbarEdge.Bottom, 360, 700, 12);
        Assert.Equal(1920 - 12 - 360, r.Left);
    }

    [Fact]
    public void TopAndSideTaskbars()
    {
        var top = FlyoutPlacement.Place(new PixelRect(1800, 10, 1820, 30), new PixelRect(0, 48, 1920, 1080), TaskbarEdge.Top, 360, 700, 12);
        Assert.Equal(48 + 12, top.Top);

        var left = FlyoutPlacement.Place(new PixelRect(10, 1000, 30, 1020), new PixelRect(60, 0, 1920, 1080), TaskbarEdge.Left, 360, 700, 12);
        Assert.Equal(60 + 12, left.Left);
        Assert.Equal(1080 - 12 - 700, left.Top);

        var right = FlyoutPlacement.Place(new PixelRect(1880, 500, 1900, 520), new PixelRect(0, 0, 1860, 1080), TaskbarEdge.Right, 360, 700, 12);
        Assert.Equal(1860 - 12 - 360, right.Left);
    }

    [Fact]
    public void AutoHideTaskbarStillGetsRoomOnItsEdge()
    {
        // Work area equals the monitor when the taskbar auto-hides, but it is showing while clicked.
        Assert.Equal(new PixelRect(0, 0, 1920, 1032), FlyoutPlacement.EffectiveWork(Monitor, Monitor, TaskbarEdge.Bottom, 48));
        Assert.Equal(new PixelRect(0, 48, 1920, 1080), FlyoutPlacement.EffectiveWork(Monitor, Monitor, TaskbarEdge.Top, 48));
        Assert.Equal(new PixelRect(60, 0, 1920, 1080), FlyoutPlacement.EffectiveWork(Monitor, Monitor, TaskbarEdge.Left, 60));
        Assert.Equal(new PixelRect(0, 0, 1860, 1080), FlyoutPlacement.EffectiveWork(Monitor, Monitor, TaskbarEdge.Right, 60));
    }

    [Fact]
    public void ReservedWorkAreaIsLeftAlone()
    {
        Assert.Equal(BottomWork, FlyoutPlacement.EffectiveWork(Monitor, BottomWork, TaskbarEdge.Bottom, 48));
    }

    [Fact]
    public void MaxHeightLeavesMargins()
    {
        Assert.Equal(1032 - 24, FlyoutPlacement.MaxHeight(BottomWork, 12));
    }
}

public class ActivityTrackerTests
{
    private static readonly TimeSpan Away = TimeSpan.FromSeconds(10);

    [Fact]
    public void ReportsReturnOnlyAfterBeingAway()
    {
        var t = new ActivityTracker(Away);
        Assert.False(t.Sample(TimeSpan.FromSeconds(1)));
        Assert.False(t.Sample(TimeSpan.FromSeconds(5)));
        Assert.False(t.Sample(TimeSpan.FromSeconds(0.2)));
        Assert.False(t.IsAway);

        Assert.False(t.Sample(TimeSpan.FromSeconds(12)));
        Assert.True(t.IsAway);
        Assert.True(t.Sample(TimeSpan.FromSeconds(0.1)));
        Assert.False(t.IsAway);
        Assert.False(t.Sample(TimeSpan.FromSeconds(0.3)));
    }

    [Fact]
    public void StillIdleIsNotAReturn()
    {
        var t = new ActivityTracker(Away);
        t.Sample(TimeSpan.FromSeconds(11));
        Assert.False(t.Sample(TimeSpan.FromSeconds(11.5)));
        Assert.True(t.IsAway);
    }
}
