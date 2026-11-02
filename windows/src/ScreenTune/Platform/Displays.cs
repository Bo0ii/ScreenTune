using System.Runtime.InteropServices;
using ScreenTune.Core;
using static ScreenTune.Interop.Native;

namespace ScreenTune.Platform;

/// <summary>One connected screen.</summary>
/// <param name="Id">Monitor device path. Stable across reconnects and reboots, unlike \\.\DISPLAYn.</param>
/// <param name="GdiName">\\.\DISPLAYn, the name gamma ramps are written to. Can change when screens are re-plugged.</param>
internal sealed record ScreenInfo(
    string Id,
    string GdiName,
    string Name,
    bool IsBuiltIn,
    bool IsPrimary,
    bool IsHdr,
    PixelRect Bounds);

internal static class Displays
{
    private sealed record Target(string Id, string FriendlyName, bool IsBuiltIn, bool IsHdr);

    /// <summary>Connected screens, primary first, then left to right.</summary>
    public static List<ScreenInfo> Current()
    {
        var monitors = new List<(string Gdi, PixelRect Bounds, bool Primary)>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr _, ref RECT _, IntPtr _) =>
        {
            var info = new MONITORINFOEXW { cbSize = (uint)Marshal.SizeOf<MONITORINFOEXW>() };
            if (GetMonitorInfoW(monitor, ref info))
            {
                monitors.Add((info.szDevice, info.rcMonitor.ToPixelRect(), (info.dwFlags & MONITORINFOF_PRIMARY) != 0));
            }
            return true;
        }, IntPtr.Zero);

        var targets = QueryTargets();
        var ordered = monitors
            .OrderByDescending(m => m.Primary)
            .ThenBy(m => m.Bounds.Left)
            .ThenBy(m => m.Bounds.Top)
            .ToList();

        var rows = ordered.Select((m, index) =>
        {
            targets.TryGetValue(m.Gdi, out var t);
            var builtIn = t?.IsBuiltIn ?? false;
            var name = ScreenNaming.NameOrFallback(t?.FriendlyName, builtIn, index);
            return (Monitor: m, Target: t, Name: name);
        }).ToList();

        var names = ScreenNaming.Disambiguate(rows.Select(r => r.Name).ToList());
        return rows.Select((r, i) => new ScreenInfo(
            Id: r.Target?.Id ?? "gdi:" + r.Monitor.Gdi,
            GdiName: r.Monitor.Gdi,
            Name: names[i],
            IsBuiltIn: r.Target?.IsBuiltIn ?? false,
            IsPrimary: r.Monitor.Primary,
            IsHdr: r.Target?.IsHdr ?? false,
            Bounds: r.Monitor.Bounds)).ToList();
    }

    // Maps \\.\DISPLAYn to the monitor behind it. In duplicate mode one source drives
    // several monitors; the first one names it, since a gamma ramp is per source anyway.
    private static Dictionary<string, Target> QueryTargets()
    {
        var result = new Dictionary<string, Target>(StringComparer.OrdinalIgnoreCase);
        DISPLAYCONFIG_PATH_INFO[] paths;
        uint pathCount, modeCount;
        int status;
        do
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out pathCount, out modeCount) != 0) return result;
            paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            status = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
        } while (status == ERROR_INSUFFICIENT_BUFFER);
        if (status != 0) return result;

        for (var i = 0; i < pathCount; i++)
        {
            var path = paths[i];
            var source = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
            {
                header = Header<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                    path.sourceInfo.adapterId, path.sourceInfo.id),
            };
            if (DisplayConfigGetDeviceInfo(ref source) != 0 || result.ContainsKey(source.viewGdiDeviceName)) continue;

            var target = new DISPLAYCONFIG_TARGET_DEVICE_NAME
            {
                header = Header<DISPLAYCONFIG_TARGET_DEVICE_NAME>(DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                    path.targetInfo.adapterId, path.targetInfo.id),
            };
            if (DisplayConfigGetDeviceInfo(ref target) != 0) continue;

            var id = !string.IsNullOrEmpty(target.monitorDevicePath)
                ? target.monitorDevicePath
                : $"edid:{target.edidManufactureId:X4}{target.edidProductCodeId:X4}:{target.connectorInstance}";
            var tech = target.outputTechnology;
            var builtIn = tech is DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL
                or DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED
                or DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED
                or DISPLAYCONFIG_OUTPUT_TECHNOLOGY_LVDS;

            result[source.viewGdiDeviceName] = new Target(id, target.monitorFriendlyDeviceName, builtIn,
                IsHdr(path.targetInfo.adapterId, path.targetInfo.id));
        }
        return result;
    }

    // Windows ignores gamma ramps while a screen is in HDR mode.
    private static bool IsHdr(LUID adapter, uint targetId)
    {
        var info2 = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2
        {
            header = Header<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2>(DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO_2, adapter, targetId),
        };
        if (DisplayConfigGetDeviceInfo(ref info2) == 0)
        {
            return info2.activeColorMode == DISPLAYCONFIG_ADVANCED_COLOR_MODE_HDR;
        }

        // Before Windows 11 24H2, "advanced color enabled" means HDR.
        var info = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
        {
            header = Header<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO>(DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO, adapter, targetId),
        };
        return DisplayConfigGetDeviceInfo(ref info) == 0 && (info.value & 0x2) != 0;
    }

    private static DISPLAYCONFIG_DEVICE_INFO_HEADER Header<T>(uint type, LUID adapter, uint id) => new()
    {
        type = type,
        size = (uint)Marshal.SizeOf<T>(),
        adapterId = adapter,
        id = id,
    };
}
