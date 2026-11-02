using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using ScreenTune.Core;
using ScreenTune.Interop;
using ScreenTune.Platform;

namespace ScreenTune;

/// <summary>
/// Screens, per-screen settings, and keeping the displays in sync with them.
/// Same shape as the macOS CalibrationModel: every change marks screens dirty and
/// one flush on the next dispatcher pass writes the displays and schedules the save.
/// </summary>
internal sealed class CalibrationModel : INotifyPropertyChanged, IDisposable
{
    private static readonly TimeSpan AwayInterval = TimeSpan.FromSeconds(10);

    private readonly DispatcherQueue _queue;
    private readonly SettingsStore _store;
    private readonly GammaController _gamma = new();
    private readonly SaturationEffect _saturation = new();
    private readonly ActivityTracker _activity = new(AwayInterval);

    private readonly Dictionary<string, DisplaySettings> _settings;
    private double _saturationAmount;
    private string? _selectedScreenId;
    private bool _isEnabled;
    private bool _isComparing;

    private readonly HashSet<string> _pendingTone = [];
    private bool _pendingSaturation;
    private bool _flushScheduled;
    private readonly HashSet<string> _blocked = [];

    private readonly DispatcherQueueTimer _saveTimer;
    private readonly DispatcherQueueTimer _activityTimer;
    private readonly DispatcherQueueTimer _displaysTimer;
    private readonly DispatcherQueueTimer _resumeTimer;
    private int _activityTicks;
    private bool _disposed;

    public CalibrationModel(DispatcherQueue queue, SettingsStore store)
    {
        _queue = queue;
        _store = store;

        var state = store.Load();
        _settings = new Dictionary<string, DisplaySettings>(state.Screens);
        _saturationAmount = state.Saturation;
        _isEnabled = state.Enabled;
        Screens = Displays.Current();
        LogScreens();
        _selectedScreenId = ValidSelection(state.SelectedScreen);

        _saveTimer = OneShot(TimeSpan.FromMilliseconds(400), Save);
        _displaysTimer = OneShot(TimeSpan.FromMilliseconds(500), ScreensChanged);
        _resumeTimer = OneShot(TimeSpan.FromMilliseconds(500), Resumed);

        // 500 ms is cheap (one GetLastInputInfo call) and catches the user coming back
        // quickly; the ramp read-back only runs every fourth tick.
        _activityTimer = queue.CreateTimer();
        _activityTimer.Interval = TimeSpan.FromMilliseconds(500);
        _activityTimer.IsRepeating = true;
        _activityTimer.Tick += (_, _) => ActivityTick();
        _activityTimer.Start();

        ApplyEverything();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<ScreenInfo> Screens { get; private set; }

    public ScreenInfo? SelectedScreen => Screens.FirstOrDefault(s => s.Id == _selectedScreenId);

    public string? SelectedScreenId
    {
        get => _selectedScreenId;
        set
        {
            if (value == _selectedScreenId) return;
            _selectedScreenId = value;
            RequestSave();
            Notify();
            Notify(nameof(SelectedScreen));
            Notify(nameof(Current));
            Notify(nameof(SelectedScreenIsBlocked));
        }
    }

    /// <summary>Settings of the screen picked in the flyout, with the shared saturation folded in.</summary>
    public DisplaySettings Current
    {
        get
        {
            var tone = _selectedScreenId is { } id ? _settings.GetValueOrDefault(id, DisplaySettings.Default) : DisplaySettings.Default;
            return tone with { Saturation = _saturationAmount };
        }
        set
        {
            if (_selectedScreenId is not { } id) return;
            var tone = value.Clamped() with { Saturation = 1 };
            var saturation = DisplaySettings.SaturationRange.Clamp(value.Saturation);
            var changed = false;

            if (!tone.HasSameTone(_settings.GetValueOrDefault(id, DisplaySettings.Default)))
            {
                if (tone == DisplaySettings.Default) _settings.Remove(id);
                else _settings[id] = tone;
                _pendingTone.Add(id);
                changed = true;
            }
            if (saturation != _saturationAmount)
            {
                _saturationAmount = saturation;
                _pendingSaturation = true;
                changed = true;
            }
            if (!changed) return;
            Notify();
            ScheduleFlush();
        }
    }

    /// <summary>Master switch. Off leaves every screen untouched but keeps the settings.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (value == _isEnabled) return;
            _isEnabled = value;
            RequestSave();
            Notify();
            ActiveStateChanged();
        }
    }

    /// <summary>True while the compare button is held: shows the original colors.</summary>
    public bool IsComparing
    {
        get => _isComparing;
        set
        {
            if (value == _isComparing) return;
            _isComparing = value;
            Notify();
            ActiveStateChanged();
        }
    }

    public bool SaturationSupported => _saturation.IsSupported;

    /// <summary>Windows refused the selected screen's curve (outside the default gamma range).</summary>
    public bool SelectedScreenIsBlocked => _selectedScreenId is { } id && _blocked.Contains(id);

    private bool IsActive => _isEnabled && !_isComparing;

    // Actions

    public void SetValue(SettingKey key, double value) => Current = Current.With(key, value);

    public void ResetCurrent() => Current = DisplaySettings.Default;

    public void ImportProfile(DisplaySettings profile) => Current = profile.Clamped();

    /// <summary>Writes every screen again, e.g. after the gamma range was unlocked.</summary>
    public void ReapplyAll() => ApplyEverything();

    /// <summary>Called by the system window; bursts are debounced like on macOS.</summary>
    public void NotifyDisplaysChanged() => Restart(_displaysTimer);

    public void NotifyResumed() => Restart(_resumeTimer);

    /// <summary>Puts every screen back the way it was and writes the settings file. Safe to call twice.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _activityTimer.Stop();
        _saveTimer.Stop();
        Save();
        _gamma.Dispose();
        _saturation.Dispose();
    }

    // Applying

    // Coalesces bursts (reset, import) into one display write. Slider drags still apply
    // every tick because each tick is its own dispatcher pass.
    private void ScheduleFlush()
    {
        if (_flushScheduled) return;
        _flushScheduled = true;
        _queue.TryEnqueue(Flush);
    }

    private void Flush()
    {
        _flushScheduled = false;
        if (_disposed) return;
        RequestSave();
        foreach (var screen in Screens.Where(s => _pendingTone.Contains(s.Id))) ApplyTone(screen);
        if (_pendingSaturation) ApplySaturation();
        _pendingTone.Clear();
        _pendingSaturation = false;
    }

    private DisplaySettings Effective(ScreenInfo screen) =>
        IsActive ? _settings.GetValueOrDefault(screen.Id, DisplaySettings.Default) : DisplaySettings.Default;

    private void ApplyTone(ScreenInfo screen)
    {
        var ok = _gamma.Apply(Effective(screen), screen.GdiName);
        var changed = ok ? _blocked.Remove(screen.Id) : _blocked.Add(screen.Id);
        if (changed && !ok) Log.Write($"Windows refused the ramp for {screen.Name} ({screen.GdiName})");
        if (changed && screen.Id == _selectedScreenId) Notify(nameof(SelectedScreenIsBlocked));
    }

    private void ApplySaturation() => _saturation.Set(IsActive ? _saturationAmount : 1);

    private void ActiveStateChanged()
    {
        if (IsActive)
        {
            ApplyEverything();
        }
        else
        {
            // One pass over the touched screens keeps hold-to-compare instant.
            _gamma.RemoveAll();
            _saturation.Remove();
        }
    }

    private void ApplyEverything()
    {
        foreach (var screen in Screens) ApplyTone(screen);
        ApplySaturation();
    }

    // Screens

    private void ScreensChanged()
    {
        if (_disposed) return;
        Screens = Displays.Current();
        LogScreens();
        _gamma.Retain(Screens.Select(s => s.GdiName));
        _blocked.IntersectWith(Screens.Select(s => s.Id));
        var validated = ValidSelection(_selectedScreenId);
        Notify(nameof(Screens));
        if (validated != _selectedScreenId) SelectedScreenId = validated;
        else Notify(nameof(SelectedScreen));
        ApplyEverything();
    }

    private void LogScreens()
    {
        foreach (var s in Screens)
        {
            Log.Write($"Screen \"{s.Name}\" {s.GdiName} primary={s.IsPrimary} builtIn={s.IsBuiltIn} hdr={s.IsHdr} " +
                      $"bounds={s.Bounds.Left},{s.Bounds.Top} {s.Bounds.Width}x{s.Bounds.Height} id={s.Id}");
        }
    }

    private string? ValidSelection(string? id)
    {
        if (id is not null && Screens.Any(s => s.Id == id)) return id;
        return Screens.FirstOrDefault(s => s.IsPrimary)?.Id ?? Screens.FirstOrDefault()?.Id;
    }

    // Drift correction

    // Windows and GPU drivers reset gamma ramps after sleep, lock, the UAC prompt,
    // full-screen games and mode changes.
    private void Resumed()
    {
        if (_disposed) return;
        _gamma.Retain(Screens.Select(s => s.GdiName));
        if (IsActive) ApplyEverything();
    }

    private void ActivityTick()
    {
        // When a KVM switches away, this PC stops getting input. The first input after
        // that means the user is back, so check the ramps right away.
        if (_activity.Sample(Native.IdleTime()))
        {
            _gamma.ReapplyDrifted();
            return;
        }
        if (++_activityTicks % 4 != 0) return;
        // Skip while away so a KVM-switched machine is not fighting anything in the background.
        if (_activity.IsAway || !_gamma.HasAppliedTables) return;
        _gamma.ReapplyDrifted();
    }

    // Persistence

    private void RequestSave() => Restart(_saveTimer);

    private void Save()
    {
        try
        {
            _store.Save(new StoredState
            {
                Enabled = _isEnabled,
                SelectedScreen = _selectedScreenId,
                Saturation = _saturationAmount,
                Screens = new Dictionary<string, DisplaySettings>(_settings),
            });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A failed write keeps the in-memory settings; the next change tries again.
        }
    }

    // Helpers

    private DispatcherQueueTimer OneShot(TimeSpan interval, Action action)
    {
        var timer = _queue.CreateTimer();
        timer.Interval = interval;
        timer.IsRepeating = false;
        timer.Tick += (_, _) => action();
        return timer;
    }

    private static void Restart(DispatcherQueueTimer timer)
    {
        timer.Stop();
        timer.Start();
    }

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
