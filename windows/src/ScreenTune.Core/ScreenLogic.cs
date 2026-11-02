namespace ScreenTune.Core;

public static class ScreenNaming
{
    /// <summary>Two identical monitors become "Dell U2720Q" and "Dell U2720Q 2".</summary>
    public static IReadOnlyList<string> Disambiguate(IReadOnlyList<string> names)
    {
        var counts = new Dictionary<string, int>();
        var result = new List<string>(names.Count);
        foreach (var name in names)
        {
            counts[name] = counts.GetValueOrDefault(name) + 1;
            result.Add(counts[name] > 1 ? $"{name} {counts[name]}" : name);
        }
        return result;
    }

    /// <summary>Laptop panels often report no EDID name.</summary>
    public static string NameOrFallback(string? name, bool isBuiltIn, int index) =>
        !string.IsNullOrWhiteSpace(name) ? name.Trim()
        : isBuiltIn ? "Built-in Display"
        : $"Display {index + 1}";
}

/// <summary>Rectangle in physical pixels, right/bottom exclusive like a Win32 RECT.</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public int CenterX => Left + Width / 2;
    public int CenterY => Top + Height / 2;
}

public enum TaskbarEdge { Bottom, Top, Left, Right }

/// <summary>Where the tray flyout goes: next to the taskbar, over the icon, inside the work area.</summary>
public static class FlyoutPlacement
{
    public static TaskbarEdge DetectEdge(PixelRect monitor, PixelRect work, PixelRect anchor)
    {
        if (work.Bottom < monitor.Bottom) return TaskbarEdge.Bottom;
        if (work.Top > monitor.Top) return TaskbarEdge.Top;
        if (work.Left > monitor.Left) return TaskbarEdge.Left;
        if (work.Right < monitor.Right) return TaskbarEdge.Right;

        // Auto-hide taskbar: the work area is the whole monitor, so go by the icon.
        var distances = new (TaskbarEdge Edge, int Distance)[]
        {
            (TaskbarEdge.Bottom, monitor.Bottom - anchor.CenterY),
            (TaskbarEdge.Top, anchor.CenterY - monitor.Top),
            (TaskbarEdge.Left, anchor.CenterX - monitor.Left),
            (TaskbarEdge.Right, monitor.Right - anchor.CenterX),
        };
        return distances.MinBy(d => d.Distance).Edge;
    }

    /// <summary>
    /// An auto-hide taskbar reserves nothing, so the work area is the whole monitor even
    /// though the taskbar is showing while its icon is clicked. Keep clear of it anyway.
    /// </summary>
    public static PixelRect EffectiveWork(PixelRect monitor, PixelRect work, TaskbarEdge edge, int taskbarThickness)
    {
        if (work != monitor || taskbarThickness <= 0) return work;
        return edge switch
        {
            TaskbarEdge.Top => work with { Top = work.Top + taskbarThickness },
            TaskbarEdge.Left => work with { Left = work.Left + taskbarThickness },
            TaskbarEdge.Right => work with { Right = work.Right - taskbarThickness },
            _ => work with { Bottom = work.Bottom - taskbarThickness },
        };
    }

    public static int MaxHeight(PixelRect work, int margin) => work.Height - 2 * margin;

    public static PixelRect Place(PixelRect anchor, PixelRect work, TaskbarEdge edge, int width, int height, int margin)
    {
        int ClampX(int x) => Math.Max(work.Left + margin, Math.Min(x, work.Right - margin - width));
        int ClampY(int y) => Math.Max(work.Top + margin, Math.Min(y, work.Bottom - margin - height));

        var (left, top) = edge switch
        {
            TaskbarEdge.Top => (ClampX(anchor.CenterX - width / 2), work.Top + margin),
            TaskbarEdge.Left => (work.Left + margin, ClampY(anchor.CenterY - height / 2)),
            TaskbarEdge.Right => (work.Right - margin - width, ClampY(anchor.CenterY - height / 2)),
            _ => (ClampX(anchor.CenterX - width / 2), work.Bottom - margin - height),
        };
        return new PixelRect(left, top, left + width, top + height);
    }
}

/// <summary>
/// Tells when the user comes back after being away (a KVM switched to another
/// machine, or they walked off). Fed with the system idle time on a timer.
/// </summary>
public sealed class ActivityTracker(TimeSpan awayInterval)
{
    private TimeSpan _lastIdle = TimeSpan.Zero;

    public bool IsAway => _lastIdle >= awayInterval;

    /// <summary>Returns true on the first sample with new input after being away.</summary>
    public bool Sample(TimeSpan idle)
    {
        var returned = IsAway && idle < _lastIdle;
        _lastIdle = idle;
        return returned;
    }
}
