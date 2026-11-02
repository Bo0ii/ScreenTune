using ScreenTune.Core;
using static ScreenTune.Interop.Native;

namespace ScreenTune.Platform;

/// <summary>
/// System-wide saturation through the Magnification API's full-screen color matrix,
/// the same mechanism Windows color filters use. Gamma ramps are per channel and
/// cannot mix channels, so they cannot desaturate.
///
/// The matrix covers the whole desktop. Windows has no public per-monitor
/// equivalent, which is why saturation is one value for every screen.
/// </summary>
internal sealed class SaturationEffect : IDisposable
{
    private bool _applied;
    private bool _disposed;
    private bool _loggedFailure;

    public SaturationEffect()
    {
        try
        {
            IsSupported = MagInitialize() && MagSetFullscreenColorEffect(ColorMatrix.Identity);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            IsSupported = false;
        }
    }

    public bool IsSupported { get; }

    public void Set(double amount)
    {
        if (!IsSupported || _disposed) return;
        if (Math.Abs(amount - 1) < DisplaySettings.SaturationThreshold)
        {
            if (_applied && MagSetFullscreenColorEffect(ColorMatrix.Identity)) _applied = false;
            return;
        }
        if (MagSetFullscreenColorEffect(ColorMatrix.Saturation(amount)))
        {
            _applied = true;
        }
        else if (!_loggedFailure)
        {
            _loggedFailure = true;
            Log.Write($"MagSetFullscreenColorEffect refused saturation {amount:0.00}");
        }
    }

    public void Remove() => Set(1);

    public void Dispose()
    {
        if (_disposed) return;
        Remove();
        if (IsSupported) MagUninitialize();
        _disposed = true;
    }
}
