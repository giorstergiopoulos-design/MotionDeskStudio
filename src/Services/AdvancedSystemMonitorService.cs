using System.Diagnostics;
using System.Net.NetworkInformation;

namespace MotionDesk.Services;

public readonly record struct AdvancedMetrics(double CpuPercent, double AvailableMemoryMb, double TotalMemoryMb, double NetworkDownKbps, double NetworkUpKbps, int ProcessCount, string BatteryMode);

public sealed class AdvancedSystemMonitorService
{
    private static readonly Lazy<AdvancedSystemMonitorService> Lazy = new(() => new());
    public static AdvancedSystemMonitorService Instance => Lazy.Value;
    private long _lastDown, _lastUp;
    private DateTime _lastNetwork = DateTime.UtcNow;
    private DateTime _lastSnapshot = DateTime.MinValue;
    private AdvancedMetrics _cached;

    public AdvancedMetrics GetSnapshot()
    {
        var now = DateTime.UtcNow;
        if (_lastSnapshot != DateTime.MinValue && (now - _lastSnapshot).TotalMilliseconds < 750)
            return _cached;

        var basic = SystemMonitorService.Instance.GetSnapshot();
        long down = 0, up = 0;
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            try
            {
                var stats = ni.GetIPv4Statistics();
                down += stats.BytesReceived;
                up += stats.BytesSent;
            }
            catch (NetworkInformationException) { }
        }

        var seconds = Math.Max(0.25, (now - _lastNetwork).TotalSeconds);
        var downK = _lastDown == 0 ? 0 : Math.Max(0, down - _lastDown) / 1024d / seconds;
        var upK = _lastUp == 0 ? 0 : Math.Max(0, up - _lastUp) / 1024d / seconds;
        _lastDown = down; _lastUp = up; _lastNetwork = now;

        _cached = new AdvancedMetrics(
            basic.CpuPercent, basic.AvailableMemoryMb, basic.TotalMemoryMb,
            downK, upK, Process.GetProcesses().Length,
            PerformanceModeManager.GetRecommendedMode());
        _lastSnapshot = now;
        return _cached;
    }
}
