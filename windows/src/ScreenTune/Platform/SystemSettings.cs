using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;

namespace ScreenTune.Platform;

/// <summary>Launch at startup through the per-user Run key, which Settings > Apps > Startup controls.</summary>
internal static class LaunchAtStartup
{
    public enum State { Off, On, TurnedOffInSettings }

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "ScreenTune";

    private static string Command => $"\"{Environment.ProcessPath}\" --background";

    public static State Get()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKey);
        if (run?.GetValue(ValueName) is not string) return State.Off;

        // Startup apps in Settings (and Task Manager) record a toggle here; an odd first byte means off.
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
        return approved?.GetValue(ValueName) is byte[] { Length: > 0 } flags && (flags[0] & 1) == 1
            ? State.TurnedOffInSettings
            : State.On;
    }

    public static void Set(bool on)
    {
        using var run = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) run.SetValue(ValueName, Command);
        else run.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>Keeps the Run entry pointing at this exe if the app folder moved.</summary>
    public static void RefreshPath()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (run?.GetValue(ValueName) is string existing && existing != Command) run.SetValue(ValueName, Command);
    }

    public static void OpenStartupAppsSettings() =>
        Process.Start(new ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true });
}

/// <summary>
/// GDI refuses gamma ramps that move far from linear (strong warmth, a big black
/// lift, very low contrast) unless the machine-wide GdiIcmGammaRange policy is 256.
/// Raising it needs administrator approval, so it is a separate, explicit step.
/// </summary>
internal static class GammaRangeUnlock
{
    private const string KeyPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM";
    private const string ValueName = "GdiIcmGammaRange";
    private const int ErrorCancelled = 1223;

    public static bool IsUnlocked
    {
        get
        {
            using var key = Registry.LocalMachine.OpenSubKey(KeyPath);
            return key?.GetValue(ValueName) is int v && v >= 256;
        }
    }

    /// <summary>Returns true when unlocked, false on failure, null when the user declined the UAC prompt.</summary>
    public static async Task<bool?> UnlockAsync()
    {
        var info = new ProcessStartInfo("reg.exe", $"add \"HKLM\\{KeyPath}\" /v {ValueName} /t REG_DWORD /d 256 /f")
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        try
        {
            using var process = Process.Start(info);
            if (process is null) return false;
            await process.WaitForExitAsync();
            return process.ExitCode == 0 && IsUnlocked;
        }
        catch (Win32Exception e) when (e.NativeErrorCode == ErrorCancelled)
        {
            return null;
        }
    }
}
