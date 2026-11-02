using System.Text.Json;

namespace ScreenTune.Core;

/// <summary>Everything ScreenTune remembers between launches.</summary>
public sealed record StoredState
{
    public int Version { get; set; } = 1;

    /// <summary>Master switch. Off leaves every screen untouched but keeps the settings.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Screen id the flyout edits.</summary>
    public string? SelectedScreen { get; set; }

    /// <summary>
    /// Windows can only filter saturation for the whole desktop, so it is one value
    /// for every screen instead of living in each screen's settings.
    /// </summary>
    public double Saturation { get; set; } = 1.0;

    /// <summary>Per-screen settings keyed by the monitor's device path.</summary>
    public Dictionary<string, DisplaySettings> Screens { get; set; } = new();
}

/// <summary>JSON file persistence. Loading never throws and always returns clamped values.</summary>
public sealed class SettingsStore(string filePath)
{
    public string FilePath { get; } = filePath;

    public StoredState Load()
    {
        StoredState? state;
        try
        {
            if (!File.Exists(FilePath)) return new StoredState();
            state = JsonSerializer.Deserialize(File.ReadAllText(FilePath), ScreenTuneJson.Default.StoredState);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new StoredState();
        }
        if (state is null) return new StoredState();

        var screens = new Dictionary<string, DisplaySettings>();
        foreach (var (id, settings) in state.Screens ?? [])
        {
            if (settings is not null) screens[id] = settings.Clamped();
        }
        var saturation = double.IsFinite(state.Saturation)
            ? DisplaySettings.SaturationRange.Clamp(state.Saturation)
            : 1.0;
        return state with { Screens = screens, Saturation = saturation };
    }

    /// <summary>Writes to a temp file and moves it over the old one, so a crash never leaves half a file.</summary>
    public void Save(StoredState state)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, ScreenTuneJson.Default.StoredState));
        File.Move(temp, FilePath, overwrite: true);
    }
}
