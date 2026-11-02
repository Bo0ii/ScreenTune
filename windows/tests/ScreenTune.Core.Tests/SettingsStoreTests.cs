using ScreenTune.Core;

namespace ScreenTune.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "screentune-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void FreshStoreHasDefaults()
    {
        var state = new SettingsStore(FilePath).Load();
        Assert.Empty(state.Screens);
        Assert.True(state.Enabled);
        Assert.Null(state.SelectedScreen);
        Assert.Equal(1.0, state.Saturation);
    }

    [Fact]
    public void SaveThenLoadRoundTrips()
    {
        var store = new SettingsStore(FilePath);
        var a = new DisplaySettings() with { Gamma = 2.0, Temperature = 4000 };
        store.Save(new StoredState
        {
            Enabled = false,
            SelectedScreen = "X",
            Saturation = 0.6,
            Screens = new() { ["X"] = a },
        });

        var loaded = new SettingsStore(FilePath).Load();
        Assert.False(loaded.Enabled);
        Assert.Equal("X", loaded.SelectedScreen);
        Assert.Equal(0.6, loaded.Saturation);
        Assert.Equal(a, loaded.Screens["X"]);
    }

    [Fact]
    public void BadStoredValuesAreClampedOnLoad()
    {
        var store = new SettingsStore(FilePath);
        store.Save(new StoredState
        {
            Saturation = 7,
            Screens = new() { ["Y"] = new DisplaySettings() with { Gamma = 99 } },
        });
        var loaded = store.Load();
        Assert.Equal(DisplaySettings.GammaRange.Max, loaded.Screens["Y"].Gamma);
        Assert.Equal(DisplaySettings.SaturationRange.Max, loaded.Saturation);
    }

    [Fact]
    public void CorruptFileFallsBackToDefaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ this is not json");
        var loaded = new SettingsStore(FilePath).Load();
        Assert.Empty(loaded.Screens);
        Assert.True(loaded.Enabled);
    }

    [Fact]
    public void NullScreenEntriesAreDropped()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """{"screens":{"A":null,"B":{"gamma":2.0}}}""");
        var loaded = new SettingsStore(FilePath).Load();
        Assert.Single(loaded.Screens);
        Assert.Equal(2.0, loaded.Screens["B"].Gamma);
    }

    [Fact]
    public void SaveLeavesNoTempFiles()
    {
        var store = new SettingsStore(FilePath);
        store.Save(new StoredState());
        store.Save(new StoredState { Enabled = false });
        Assert.Equal(["settings.json"], Directory.GetFiles(_dir).Select(Path.GetFileName));
    }
}
