using ScreenTune.Core;
using static ScreenTune.Interop.Native;

namespace ScreenTune.Platform;

/// <summary>
/// Writes per-screen gamma ramps and re-writes them when Windows or the driver resets them.
///
/// Unlike macOS, Windows keeps a ramp after the app exits and has no "restore the
/// system calibration" call. So the ramp each screen had before ScreenTune first
/// touched it (which includes any ICC calibration loaded by Windows) is kept and put
/// back on compare, on reset to defaults, and on quit.
/// </summary>
internal sealed class GammaController : IDisposable
{
    private sealed class Device(string gdiName)
    {
        public string GdiName { get; } = gdiName;
        public IntPtr Hdc;
        public ushort[]? Original;
        public ushort[]? Applied;
        public bool Blocked;
    }

    private readonly Dictionary<string, Device> _devices = new(StringComparer.OrdinalIgnoreCase);

    public bool HasAppliedTables => _devices.Values.Any(d => d.Applied is not null);

    /// <summary>
    /// Returns false when Windows refused the ramp. By default GDI rejects curves that
    /// move too far from linear unless the GdiIcmGammaRange policy is raised.
    /// </summary>
    public bool Apply(DisplaySettings settings, string gdiName)
    {
        // Identity settings leave the screen untouched so Night Light and the
        // screen's own calibration keep working on untuned screens.
        if (settings.IsToneIdentity)
        {
            if (_devices.TryGetValue(gdiName, out var existing) && existing.Applied is not null)
            {
                existing.Applied = null;
                existing.Blocked = false;
                Write(existing, existing.Original ?? GammaRamp.Identity());
            }
            return true;
        }

        var device = Get(gdiName);
        var ramp = GammaRamp.FromSettings(settings);
        if (device.Original is null)
        {
            // If the screen already shows exactly this curve, a previous run that did not
            // quit cleanly left it there, so linear is the real original.
            var current = Read(device);
            device.Original = current is null || GammaRamp.Matches(current, ramp) ? GammaRamp.Identity() : current;
        }
        device.Applied = ramp;
        device.Blocked = !Write(device, ramp);
        return !device.Blocked;
    }

    /// <summary>Puts every touched screen back to its original ramp. Used for off, compare and quit.</summary>
    public void RemoveAll()
    {
        foreach (var device in _devices.Values.Where(d => d.Applied is not null))
        {
            device.Applied = null;
            device.Blocked = false;
            Write(device, device.Original ?? GammaRamp.Identity());
        }
    }

    /// <summary>Forgets screens that are gone and drops cached device contexts after a display change.</summary>
    public void Retain(IEnumerable<string> gdiNames)
    {
        var keep = new HashSet<string>(gdiNames, StringComparer.OrdinalIgnoreCase);
        foreach (var name in _devices.Keys.Where(k => !keep.Contains(k)).ToList())
        {
            ReleaseDc(_devices[name]);
            _devices.Remove(name);
        }
        foreach (var device in _devices.Values) ReleaseDc(device);
    }

    public void ReapplyAll()
    {
        foreach (var device in _devices.Values)
        {
            if (device.Applied is not null) device.Blocked = !Write(device, device.Applied);
        }
    }

    public void ReapplyDrifted()
    {
        foreach (var device in _devices.Values)
        {
            if (device.Applied is null || device.Blocked) continue;
            var current = Read(device);
            if (current is null || !GammaRamp.Matches(current, device.Applied))
            {
                device.Blocked = !Write(device, device.Applied);
            }
        }
    }

    public void Dispose()
    {
        RemoveAll();
        foreach (var device in _devices.Values) ReleaseDc(device);
        _devices.Clear();
    }

    private Device Get(string gdiName)
    {
        if (!_devices.TryGetValue(gdiName, out var device))
        {
            device = new Device(gdiName);
            _devices[gdiName] = device;
        }
        return device;
    }

    private static IntPtr Dc(Device device)
    {
        if (device.Hdc == IntPtr.Zero) device.Hdc = CreateDCW(device.GdiName, null, null, IntPtr.Zero);
        return device.Hdc;
    }

    private static void ReleaseDc(Device device)
    {
        if (device.Hdc != IntPtr.Zero) DeleteDC(device.Hdc);
        device.Hdc = IntPtr.Zero;
    }

    private static bool Write(Device device, ushort[] ramp)
    {
        var hdc = Dc(device);
        return hdc != IntPtr.Zero && SetDeviceGammaRamp(hdc, ramp);
    }

    private static ushort[]? Read(Device device)
    {
        var hdc = Dc(device);
        if (hdc == IntPtr.Zero) return null;
        var ramp = new ushort[GammaRamp.Length];
        return GetDeviceGammaRamp(hdc, ramp) ? ramp : null;
    }
}
