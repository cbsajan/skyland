using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace DynamicIsland.Services;

/// <param name="Cpu">Total CPU load, 0-100.</param>
/// <param name="Ram">Physical memory in use, 0-100.</param>
/// <param name="Gpu">Busiest GPU engine, 0-100; null if Windows exposes no GPU counters.</param>
/// <param name="BatteryPercent">null on PCs without a battery.</param>
/// <param name="PluggedIn">True when on AC power.</param>
/// <param name="DownloadBytesPerSec">Bytes received per second across all active NICs; null before the first delta.</param>
/// <param name="UploadBytesPerSec">Bytes sent per second across all active NICs; null before the first delta.</param>
public sealed record SystemStats(double? Cpu, double? Ram, double? Gpu, int? BatteryPercent, bool PluggedIn,
    long? DownloadBytesPerSec = null, long? UploadBytesPerSec = null);

/// <summary>
/// Reads CPU, RAM, GPU and battery from built-in Windows APIs (no admin, no vendor tools).
/// Call <see cref="Baseline"/> before the first <see cref="Sample"/>; CPU and GPU are
/// deltas between two samples. Not thread-safe: call from one task at a time.
/// </summary>
public sealed class SystemStatsService
{
    // CPU
    private ulong _prevIdle, _prevKernel, _prevUser;
    private bool _hasCpuBaseline;

    // GPU
    private readonly Dictionary<string, CounterSample> _prevGpu = new();
    private bool? _gpuAvailable;

    // Network
    private long _prevRecv = -1, _prevSent = -1;
    private long _prevNetTicks;

    public void Baseline()
    {
        _hasCpuBaseline = ReadCpuTimes(out _prevIdle, out _prevKernel, out _prevUser);
        _prevGpu.Clear();
        ReadNetTotals(out _prevRecv, out _prevSent);
        _prevNetTicks = Environment.TickCount64;
    }

    public SystemStats Sample()
    {
        var (battery, plugged) = ReadBattery();
        var (down, up) = SampleNet();
        return new SystemStats(SampleCpu(), SampleRam(), SampleGpu(), battery, plugged, down, up);
    }

    // ----- CPU -----------------------------------------------------------

    private double? SampleCpu()
    {
        if (!ReadCpuTimes(out ulong idle, out ulong kernel, out ulong user)) return null;

        double? result = null;
        if (_hasCpuBaseline)
        {
            // Kernel time already includes idle time.
            ulong total = (kernel - _prevKernel) + (user - _prevUser);
            ulong idleDelta = idle - _prevIdle;
            if (total > 0)
                result = Math.Clamp((total - idleDelta) * 100.0 / total, 0, 100);
        }

        _prevIdle = idle; _prevKernel = kernel; _prevUser = user;
        _hasCpuBaseline = true;
        return result;
    }

    // ----- RAM -----------------------------------------------------------

    private static double? SampleRam()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref status) ? status.dwMemoryLoad : null;
    }

    // ----- GPU -----------------------------------------------------------

    private double? SampleGpu()
    {
        try
        {
            _gpuAvailable ??= PerformanceCounterCategory.Exists("GPU Engine");
            if (_gpuAvailable != true) return null;

            var category = new PerformanceCounterCategory("GPU Engine");
            var data = category.ReadCategory();
            if (!data.Contains("Utilization Percentage")) return null;

            // Sum all processes per (GPU, engine type), then take the busiest engine,
            // which is how Task Manager reports GPU load.
            var perEngine = new Dictionary<string, double>();
            var current = new Dictionary<string, CounterSample>();

            foreach (InstanceData instance in data["Utilization Percentage"].Values)
            {
                string name = instance.InstanceName;
                var sample = instance.Sample;
                current[name] = sample;

                if (!_prevGpu.TryGetValue(name, out var previous)) continue;

                double value = CounterSampleCalculator.ComputeCounterValue(previous, sample);
                string? key = EngineKey(name);
                if (key is null) continue;

                perEngine[key] = perEngine.GetValueOrDefault(key) + value;
            }

            _prevGpu.Clear();
            foreach (var kv in current) _prevGpu[kv.Key] = kv.Value;

            return perEngine.Count == 0 ? null : Math.Clamp(perEngine.Values.Max(), 0, 100);
        }
        catch
        {
            _gpuAvailable = false; // counters unavailable or broken; stop trying
            return null;
        }
    }

    // "pid_12_luid_0x0_0xD1E5_phys_0_eng_0_engtype_3D" -> "luid_0x0_0xD1E5|3D"
    private static string? EngineKey(string instanceName)
    {
        int luid = instanceName.IndexOf("luid_", StringComparison.Ordinal);
        int phys = instanceName.IndexOf("_phys_", StringComparison.Ordinal);
        int type = instanceName.IndexOf("engtype_", StringComparison.Ordinal);
        if (luid < 0 || phys < luid || type < 0) return null;

        return $"{instanceName[luid..phys]}|{instanceName[(type + "engtype_".Length)..]}";
    }

    // ----- Network (download / upload) ----------------------------------

    private (long? Down, long? Up) SampleNet()
    {
        if (!ReadNetTotals(out long recv, out long sent)) return (null, null);

        long? down = null, up = null;
        long now = Environment.TickCount64;
        long elapsed = now - _prevNetTicks;

        if (_prevRecv >= 0 && elapsed > 0)
        {
            down = Math.Max(0, (recv - _prevRecv) * 1000 / elapsed);
            up = Math.Max(0, (sent - _prevSent) * 1000 / elapsed);
        }

        _prevRecv = recv; _prevSent = sent;
        _prevNetTicks = now;
        return (down, up);
    }

    /// <summary>Sums BytesReceived / BytesSent across all operational, non-loopback NICs.</summary>
    private static bool ReadNetTotals(out long recv, out long sent)
    {
        recv = 0; sent = 0;
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                var stats = nic.GetIPStatistics();
                recv += stats.BytesReceived;
                sent += stats.BytesSent;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ----- Battery -------------------------------------------------------

    /// <summary>Battery percent (null if no battery) and whether AC power is connected.</summary>
    public static (int? Percent, bool PluggedIn) ReadBattery()
    {
        if (!GetSystemPowerStatus(out var s)) return (null, false);

        const byte NoBattery = 128;
        const byte Unknown = 255;
        bool plugged = s.ACLineStatus == 1;

        if ((s.BatteryFlag & NoBattery) != 0 || s.BatteryLifePercent == Unknown)
            return (null, plugged);

        return (s.BatteryLifePercent, plugged);
    }

    // ----- P/Invoke ------------------------------------------------------

    private static bool ReadCpuTimes(out ulong idle, out ulong kernel, out ulong user)
    {
        bool ok = GetSystemTimes(out var i, out var k, out var u);
        idle = ToUlong(i); kernel = ToUlong(k); user = ToUlong(u);
        return ok;
    }

    private static ulong ToUlong(FILETIME t) => ((ulong)t.High << 32) | t.Low;

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME { public uint Low; public uint High; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
}
