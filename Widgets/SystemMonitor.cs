using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MINWallPaper.Widgets;

internal sealed class DiskSnapshot
{
    public string Name { get; init; } = "";
    public string Label { get; init; } = "";
    public double UsedGb { get; init; }
    public double TotalGb { get; init; }
    public float Percent { get; init; }
}

/// <summary>一次系统状态采样，绘制时只读这份快照。</summary>
internal sealed class MonitorSnapshot
{
    public bool Ready { get; init; }
    public float CpuPercent { get; init; }
    public float[] CpuCores { get; init; } = Array.Empty<float>();
    public float[] CpuHistory { get; init; } = Array.Empty<float>();
    public string CpuName { get; init; } = "";
    public int Threads { get; init; }

    public double MemTotalGb { get; init; }
    public double MemUsedGb { get; init; }
    public double MemPercent { get; init; }
    public double MemAppsGb { get; init; }
    public double MemKernelGb { get; init; }
    public double MemCacheGb { get; init; }
    public double MemFreeGb { get; init; }

    public bool GpuAvailable { get; init; }
    public string GpuName { get; init; } = "";
    public float GpuPercent { get; init; }
    public float GpuTempC { get; init; }
    public double GpuMemUsedGb { get; init; }
    public double GpuMemTotalGb { get; init; }

    public List<DiskSnapshot> Disks { get; init; } = new();
}

/// <summary>
/// 后台每秒采样一次 CPU / 内存 / GPU / 磁盘。
/// 组件绘制只读快照，不在 UI 线程里跑 nvidia-smi 之类的重活。
/// </summary>
internal static class SystemMonitor
{
    private const int SystemProcessorPerformanceInformation = 8;
    private const double Gb = 1024d * 1024d * 1024d;

    private static readonly object Gate = new();
    private static readonly List<float> History = new();

    private static MonitorSnapshot _snapshot = new() { Ready = false };
    private static System.Threading.Timer? _timer;
    private static long[] _prevIdle = Array.Empty<long>();
    private static long[] _prevTotal = Array.Empty<long>();
    private static string? _cpuName;
    private static DateTime _lastGpu = DateTime.MinValue;
    private static DateTime _lastDisk = DateTime.MinValue;
    private static GpuReading? _gpu;
    private static List<DiskSnapshot> _disks = new();
    private static int _gpuUsers;

    /// <summary>监控还没采到数据时用来撑起预览。</summary>
    public static readonly MonitorSnapshot Demo = new()
    {
        Ready = false,
        CpuPercent = 32,
        CpuCores = Enumerable.Range(0, 12)
            .Select(i => 0.18f + 0.55f * Math.Abs((float)Math.Sin(i * 1.7)))
            .ToArray(),
        CpuHistory = Enumerable.Range(0, 40)
            .Select(i => 22f + 18f * (float)Math.Sin(i * 0.42) + 6f * (float)Math.Cos(i * 1.3))
            .ToArray(),
        CpuName = "CPU",
        Threads = Environment.ProcessorCount,
        MemTotalGb = 16,
        MemUsedGb = 7.2,
        MemPercent = 45,
        MemAppsGb = 4.6,
        MemKernelGb = 1.6,
        MemCacheGb = 1.0,
        MemFreeGb = 8.8,
        GpuAvailable = true,
        GpuName = "GPU",
        GpuPercent = 28,
        GpuTempC = 58,
        GpuMemUsedGb = 2.4,
        GpuMemTotalGb = 8,
        Disks = new List<DiskSnapshot>
        {
            new() { Name = "C:", Label = "系统", UsedGb = 128, TotalGb = 256, Percent = 50 }
        }
    };

    public static MonitorSnapshot Snapshot
    {
        get
        {
            EnsureStarted();
            lock (Gate)
            {
                return _snapshot;
            }
        }
    }

    /// <summary>只有存在 GPU 组件时才去轮询 nvidia-smi。</summary>
    public static void SetGpuPolling(bool enabled)
    {
        lock (Gate)
        {
            _gpuUsers = Math.Max(0, _gpuUsers + (enabled ? 1 : -1));
        }
    }

    private static void EnsureStarted()
    {
        if (_timer != null)
        {
            return;
        }

        lock (Gate)
        {
            if (_timer != null)
            {
                return;
            }

            _cpuName = ReadCpuName();
            _timer = new System.Threading.Timer(_ => Update(), null, 0, 1000);
        }
    }

    private static void Update()
    {
        try
        {
            double memTotal = 0;
            double memUsed = 0;
            double memFree = 0;
            double memCache = 0;
            double memKernel = 0;

            SampleCpu(out float cpu, out float[] cores, out bool cpuReady);
            SampleMemory(out memTotal, out memUsed, out memFree, out memCache, out memKernel);

            if (_gpuUsers > 0 && DateTime.UtcNow - _lastGpu > TimeSpan.FromSeconds(2))
            {
                _gpu = ReadGpu();
                _lastGpu = DateTime.UtcNow;
            }

            if (DateTime.UtcNow - _lastDisk > TimeSpan.FromSeconds(10))
            {
                _disks = ReadDisks();
                _lastDisk = DateTime.UtcNow;
            }

            GpuReading? gpu;
            lock (Gate)
            {
                gpu = _gpu;
            }

            var snapshot = new MonitorSnapshot
            {
                Ready = cpuReady,
                CpuPercent = cpu,
                CpuCores = cores,
                CpuHistory = BuildHistory(cpu),
                CpuName = _cpuName ?? "CPU",
                Threads = Environment.ProcessorCount,
                MemTotalGb = memTotal,
                MemUsedGb = memUsed,
                MemPercent = memTotal > 0 ? memUsed / memTotal * 100 : 0,
                MemAppsGb = memUsed,
                MemKernelGb = memKernel,
                MemCacheGb = memCache,
                MemFreeGb = memFree,
                GpuAvailable = gpu != null,
                GpuName = gpu?.Name ?? "",
                GpuPercent = gpu?.Percent ?? 0,
                GpuTempC = gpu?.TempC ?? 0,
                GpuMemUsedGb = gpu?.MemUsedGb ?? 0,
                GpuMemTotalGb = gpu?.MemTotalGb ?? 0,
                Disks = _disks
            };

            lock (Gate)
            {
                _snapshot = snapshot;
            }
        }
        catch (Exception ex)
        {
            Log.Write("系统监控采样失败: " + ex.Message);
        }
    }

    private static float[] BuildHistory(float current)
    {
        lock (Gate)
        {
            History.Add(current);
            while (History.Count > 40)
            {
                History.RemoveAt(0);
            }

            return History.ToArray();
        }
    }

    private static void SampleCpu(out float percent, out float[] cores, out bool ready)
    {
        percent = 0;
        cores = Array.Empty<float>();
        ready = false;

        int count = Math.Max(1, Environment.ProcessorCount);
        int size = Marshal.SizeOf<ProcessorPerformance>();
        IntPtr buffer = Marshal.AllocHGlobal(size * count);

        try
        {
            int status = NtQuerySystemInformation(SystemProcessorPerformanceInformation,
                buffer, size * count, out _);

            if (status != 0)
            {
                return;
            }

            var idle = new long[count];
            var total = new long[count];

            for (int i = 0; i < count; i++)
            {
                var info = Marshal.PtrToStructure<ProcessorPerformance>(buffer + i * size);
                idle[i] = info.IdleTime;
                total[i] = info.KernelTime + info.UserTime;
            }

            if (_prevIdle.Length != count)
            {
                _prevIdle = idle;
                _prevTotal = total;
                cores = Enumerable.Repeat(0.25f, count).ToArray();
                return;
            }

            var result = new float[count];
            float sum = 0;

            for (int i = 0; i < count; i++)
            {
                long idleDelta = idle[i] - _prevIdle[i];
                long totalDelta = total[i] - _prevTotal[i];
                float usage = totalDelta > 0
                    ? Math.Clamp(1f - idleDelta / (float)totalDelta, 0f, 1f)
                    : 0f;
                result[i] = usage;
                sum += usage;
            }

            _prevIdle = idle;
            _prevTotal = total;
            cores = result;
            percent = sum / count * 100f;
            ready = true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void SampleMemory(out double totalGb, out double usedGb, out double freeGb,
        out double cacheGb, out double kernelGb)
    {
        totalGb = 0;
        usedGb = 0;
        freeGb = 0;
        cacheGb = 0;
        kernelGb = 0;

        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };

        if (!GlobalMemoryStatusEx(ref status))
        {
            return;
        }

        totalGb = status.TotalPhys / Gb;
        freeGb = status.AvailPhys / Gb;
        usedGb = Math.Max(0, totalGb - freeGb);

        if (GetPerformanceInfo(out PerformanceInfo info, (uint)Marshal.SizeOf<PerformanceInfo>()))
        {
            double page = info.PageSize.ToInt64();
            double cache = info.SystemCache.ToInt64() * page / Gb;
            double kernel = (info.KernelPaged.ToInt64() + info.KernelNonpaged.ToInt64()) * page / Gb;

            cacheGb = Math.Min(cache, usedGb);
            kernelGb = Math.Min(kernel, Math.Max(0, usedGb - cacheGb));
        }
    }

    private static GpuReading? ReadGpu()
    {
        try
        {
            var start = new ProcessStartInfo("nvidia-smi")
            {
                Arguments = "--query-gpu=name,utilization.gpu,temperature.gpu,memory.used,memory.total " +
                            "--format=csv,noheader,nounits",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using Process? process = Process.Start(start);

            if (process == null)
            {
                return null;
            }

            string? line = process.StandardOutput.ReadLine();
            process.WaitForExit(2000);

            if (string.IsNullOrWhiteSpace(line))
            {
                return null;
            }

            string[] parts = line.Split(',').Select(p => p.Trim()).ToArray();

            if (parts.Length < 5)
            {
                return null;
            }

            float Parse(int index)
            {
                return float.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture,
                    out float value)
                    ? value
                    : 0;
            }

            return new GpuReading
            {
                Name = parts[0],
                Percent = Math.Clamp(Parse(1), 0, 100),
                TempC = Parse(2),
                MemUsedGb = Parse(3) / 1024d,
                MemTotalGb = Parse(4) / 1024d
            };
        }
        catch
        {
            return null;
        }
    }

    private static List<DiskSnapshot> ReadDisks()
    {
        var result = new List<DiskSnapshot>();

        try
        {
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
                    {
                        continue;
                    }

                    double total = drive.TotalSize / Gb;

                    if (total < 0.5)
                    {
                        continue;
                    }

                    double free = drive.TotalFreeSpace / Gb;
                    string label = "";

                    try
                    {
                        label = drive.VolumeLabel;
                    }
                    catch
                    {
                        // 某些分区读卷标会抛异常
                    }

                    result.Add(new DiskSnapshot
                    {
                        Name = drive.Name.TrimEnd('\\'),
                        Label = label,
                        UsedGb = total - free,
                        TotalGb = total,
                        Percent = (float)((total - free) / total * 100)
                    });
                }
                catch
                {
                    // 跳过读不到的分区
                }
            }
        }
        catch (Exception ex)
        {
            Log.Write("读取磁盘信息失败: " + ex.Message);
        }

        return result.OrderBy(d => d.Name).Take(6).ToList();
    }

    private static string? ReadCpuName()
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");

            return (key?.GetValue("ProcessorNameString") as string)?.Trim();
        }
        catch
        {
            return null;
        }
    }

    private sealed class GpuReading
    {
        public string Name { get; init; } = "";
        public float Percent { get; init; }
        public float TempC { get; init; }
        public double MemUsedGb { get; init; }
        public double MemTotalGb { get; init; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessorPerformance
    {
        public long IdleTime;
        public long KernelTime;
        public long UserTime;
        public long DpcTime;
        public long InterruptTime;
        public uint InterruptCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PerformanceInfo
    {
        public uint Size;
        public IntPtr CommitTotal;
        public IntPtr CommitLimit;
        public IntPtr CommitPeak;
        public IntPtr PhysicalTotal;
        public IntPtr PhysicalAvailable;
        public IntPtr SystemCache;
        public IntPtr KernelTotal;
        public IntPtr KernelPaged;
        public IntPtr KernelNonpaged;
        public IntPtr PageSize;
        public uint HandleCount;
        public uint ProcessCount;
        public uint ThreadCount;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int infoClass, IntPtr info, int length,
        out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool GetPerformanceInfo(out PerformanceInfo info, uint size);
}
