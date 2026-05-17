using System.Runtime.InteropServices;

using ChurchPresenter.Interop;
using ChurchPresenter.Resources;

using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Adapters.Display;

/// <summary>
/// Enumerates displays for output targeting. Prefers active display-path metadata so the app can
/// capture friendly names, refresh rates, and primary-display state, then falls back to plain Win32
/// monitor enumeration when richer metadata is unavailable. Uses full monitor bounds rather than
/// work areas so fullscreen output can cover taskbar space.
/// </summary>
public sealed class MonitorService(ILogger<MonitorService> logger) : IMonitorService
{
    private readonly ILogger<MonitorService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public IReadOnlyList<MonitorInfoDto> GetMonitors()
    {
        try
        {
            IReadOnlyList<MonitorInfoDto> configured = MapFromDisplayConfig();
            if (configured.Count > 0)
                return configured;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Display-path monitor enumeration failed; using Win32 monitor enumeration.");
        }

        IReadOnlyList<MonitorInfoDto> win32 = MapFromWin32();
        if (win32.Count > 0)
            return win32;

        _logger.LogWarning(AppLogMessages.MonitorEnumerationFailed);
        return Array.Empty<MonitorInfoDto>();
    }

    private static IReadOnlyList<MonitorInfoDto> MapFromDisplayConfig()
    {
        int result = DisplayMonitorInterop.GetDisplayConfigBufferSizes(
            DisplayMonitorInterop.QdcOnlyActivePaths,
            out uint pathCount,
            out uint modeCount);
        if (result != 0 || pathCount == 0 || modeCount == 0)
            return Array.Empty<MonitorInfoDto>();

        var paths = new DisplayMonitorInterop.DISPLAYCONFIG_PATH_INFO[pathCount];
        var modes = new DisplayMonitorInterop.DISPLAYCONFIG_MODE_INFO[modeCount];
        result = DisplayMonitorInterop.QueryDisplayConfig(
            DisplayMonitorInterop.QdcOnlyActivePaths,
            ref pathCount,
            paths,
            ref modeCount,
            modes,
            IntPtr.Zero);
        if (result != 0 || pathCount == 0)
            return Array.Empty<MonitorInfoDto>();

        var modeLookup = modes
            .Take(checked((int)modeCount))
            .Select((mode, index) => (mode, index))
            .ToDictionary(item => item.index, item => item.mode);

        var rows = new List<ConfiguredMonitor>();
        foreach (var path in paths.Take(checked((int)pathCount)))
        {
            if (path.sourceInfo.modeInfoIdx == DisplayMonitorInterop.DisplayConfigPathModeIdxInvalid)
                continue;
            if (!modeLookup.TryGetValue(checked((int)path.sourceInfo.modeInfoIdx), out var modeInfo))
                continue;
            if (modeInfo.infoType != DisplayMonitorInterop.DisplayConfigModeInfoType.Source)
                continue;

            DisplayMonitorInterop.DISPLAYCONFIG_SOURCE_MODE sourceMode = modeInfo.modeInfo.sourceMode;
            int width = checked((int)sourceMode.width);
            int height = checked((int)sourceMode.height);
            int x = sourceMode.position.x;
            int y = sourceMode.position.y;
            string name = GetFriendlyDisplayName(path.targetInfo.adapterId, path.targetInfo.id);

            rows.Add(
                new ConfiguredMonitor(
                    Name: string.IsNullOrWhiteSpace(name) ? null : name,
                    Width: width,
                    Height: height,
                    X: x,
                    Y: y,
                    IsPrimary: x == 0 && y == 0,
                    RefreshRate: ToRefreshRate(path.targetInfo.refreshRate)));
        }

        if (rows.Count == 0)
            return Array.Empty<MonitorInfoDto>();

        List<ConfiguredMonitor> ordered = rows
            .OrderBy(monitor => monitor.X)
            .ThenBy(monitor => monitor.Y)
            .ToList();

        var list = new List<MonitorInfoDto>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            ConfiguredMonitor monitor = ordered[i];
            list.Add(
                new MonitorInfoDto(
                    i,
                    string.IsNullOrWhiteSpace(monitor.Name) ? $"Display {i + 1}" : monitor.Name,
                    monitor.Width,
                    monitor.Height,
                    monitor.X,
                    monitor.Y,
                    monitor.IsPrimary,
                    monitor.RefreshRate));
        }

        return list;
    }

    private static IReadOnlyList<MonitorInfoDto> MapFromWin32()
    {
        var rows = new List<(DisplayMonitorInterop.RECT Monitor, bool Primary)>();
        bool Callback(IntPtr hMonitor, IntPtr hdcMonitor, ref DisplayMonitorInterop.RECT lprcMonitor, IntPtr dwData)
        {
            var mi = new DisplayMonitorInterop.MONITORINFO { cbSize = Marshal.SizeOf<DisplayMonitorInterop.MONITORINFO>() };
            if (!DisplayMonitorInterop.GetMonitorInfo(hMonitor, ref mi))
                return true;

            rows.Add((mi.rcMonitor, (mi.dwFlags & DisplayMonitorInterop.MonitorinfofPrimary) != 0));
            return true;
        }

        if (!DisplayMonitorInterop.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero) || rows.Count == 0)
            return Array.Empty<MonitorInfoDto>();

        List<(DisplayMonitorInterop.RECT Monitor, bool Primary)> ordered = rows
            .OrderBy(r => r.Monitor.Left)
            .ThenBy(r => r.Monitor.Top)
            .ToList();
        var list = new List<MonitorInfoDto>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            DisplayMonitorInterop.RECT w = ordered[i].Monitor;
            int width = w.Right - w.Left;
            int height = w.Bottom - w.Top;
            list.Add(
                new MonitorInfoDto(
                    i,
                    $"Display {i + 1}",
                    width,
                    height,
                    w.Left,
                    w.Top,
                    ordered[i].Primary,
                    RefreshRate: null));
        }

        return list;
    }

    private static string GetFriendlyDisplayName(DisplayMonitorInterop.LUID adapterId, uint targetId)
    {
        var targetName = new DisplayMonitorInterop.DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            header = new DisplayMonitorInterop.DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DisplayMonitorInterop.DisplayConfigDeviceInfoType.GetTargetName,
                size = (uint)Marshal.SizeOf<DisplayMonitorInterop.DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                adapterId = adapterId,
                id = targetId,
            },
            monitorFriendlyDeviceName = string.Empty,
            monitorDevicePath = string.Empty,
        };

        int result = DisplayMonitorInterop.DisplayConfigGetDeviceInfo(ref targetName);
        return result == 0
            ? targetName.monitorFriendlyDeviceName?.Trim() ?? string.Empty
            : string.Empty;
    }

    private static uint? ToRefreshRate(DisplayMonitorInterop.DISPLAYCONFIG_RATIONAL refreshRate)
    {
        if (refreshRate.Numerator == 0 || refreshRate.Denominator == 0)
            return null;

        return (uint)Math.Round(refreshRate.Numerator / (double)refreshRate.Denominator);
    }

    private readonly record struct ConfiguredMonitor(
        string? Name,
        int Width,
        int Height,
        int X,
        int Y,
        bool IsPrimary,
        uint? RefreshRate);
}