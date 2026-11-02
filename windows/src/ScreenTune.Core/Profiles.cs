using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenTune.Core;

/// <summary>
/// Profile files: the 13 DisplaySettings values as flat camelCase JSON, the same
/// format the macOS app reads and writes. Missing keys fall back to defaults.
/// </summary>
public static class Profiles
{
    public static string Encode(DisplaySettings settings) =>
        JsonSerializer.Serialize(settings, ScreenTuneJson.Default.DisplaySettings);

    /// <exception cref="JsonException">The text is not a JSON object of numbers.</exception>
    public static DisplaySettings Decode(string json)
    {
        var settings = JsonSerializer.Deserialize(json, ScreenTuneJson.Default.DisplaySettings)
            ?? throw new JsonException("The file is empty.");
        return settings.Clamped();
    }
}

// Source-generated so the published app can be trimmed without losing JSON members.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(DisplaySettings))]
[JsonSerializable(typeof(StoredState))]
internal sealed partial class ScreenTuneJson : JsonSerializerContext;
