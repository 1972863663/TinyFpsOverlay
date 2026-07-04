using System.Globalization;
using System.IO;
using LibreHardwareMonitor.Hardware;
using TinyFpsOverlay.Models;

namespace TinyFpsOverlay.Services;

public sealed class HardwareMonitorService : IDisposable
{
    private readonly Computer _computer;
    private readonly object _lock = new();
    private bool _disposed;

    public HardwareMonitorService()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = false,
            IsMotherboardEnabled = true,
            IsControllerEnabled = false,
            IsNetworkEnabled = false,
            IsStorageEnabled = false,
            IsBatteryEnabled = false,
            IsPsuEnabled = false
        };

        _computer.Open();
    }

    public MetricsSnapshot Read(double? fps)
    {
        lock (_lock)
        {
            float? cpuUsage = null;
            float? cpuTemp = null;
            float? gpuUsage = null;
            float? gpuTemp = null;
            string? gpuName = null;

            foreach (IHardware hardware in _computer.Hardware)
            {
                UpdateHardwareTree(hardware);

                bool isCpu = hardware.HardwareType == HardwareType.Cpu;
                bool isGpu = hardware.HardwareType == HardwareType.GpuAmd
                             || hardware.HardwareType == HardwareType.GpuNvidia
                             || hardware.HardwareType == HardwareType.GpuIntel;

                if (!isCpu && !isGpu)
                {
                    continue;
                }

                var sensors = EnumerateSensors(hardware).ToArray();

                if (isCpu)
                {
                    cpuUsage = PickUsage(sensors, preferTotal: true) ?? cpuUsage;
                    cpuTemp = PickTemperature(sensors, cpu: true) ?? cpuTemp;
                }
                else if (isGpu)
                {
                    gpuName ??= hardware.Name;
                    bool incomingIsDiscrete = hardware.HardwareType is HardwareType.GpuAmd or HardwareType.GpuNvidia;
                    bool currentLooksDiscrete = gpuName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)
                                                || gpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase)
                                                || gpuName.Contains("Radeon", StringComparison.OrdinalIgnoreCase)
                                                || gpuName.Contains("GeForce", StringComparison.OrdinalIgnoreCase);

                    if (gpuUsage is null || incomingIsDiscrete || !currentLooksDiscrete)
                    {
                        gpuUsage = PickUsage(sensors, preferTotal: true) ?? gpuUsage;
                        gpuTemp = PickTemperature(sensors, cpu: false) ?? gpuTemp;
                        gpuName = hardware.Name;
                    }
                }
            }

            return new MetricsSnapshot
            {
                Fps = fps,
                CpuUsage = cpuUsage,
                CpuTemperature = cpuTemp,
                GpuUsage = gpuUsage,
                GpuTemperature = gpuTemp,
                GpuName = gpuName,
                Timestamp = DateTime.Now
            };
        }
    }

    public string DumpSensors()
    {
        lock (_lock)
        {
            var lines = new List<string>
            {
                $"TinyFpsOverlay sensor dump {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                "Format: HardwareType | HardwareName | SensorType | SensorName | Value",
                ""
            };

            foreach (var hardware in _computer.Hardware)
            {
                UpdateHardwareTree(hardware);
                DumpHardware(lines, hardware, 0);
            }

            return string.Join(Environment.NewLine, lines);
        }
    }

    private static void DumpHardware(List<string> lines, IHardware hardware, int depth)
    {
        string indent = new(' ', depth * 2);
        lines.Add($"{indent}[{hardware.HardwareType}] {hardware.Name}");

        foreach (var sensor in hardware.Sensors.OrderBy(s => s.SensorType).ThenBy(s => s.Name))
        {
            string value = sensor.Value.HasValue
                ? sensor.Value.Value.ToString("0.###", CultureInfo.InvariantCulture)
                : "null";
            lines.Add($"{indent}  {sensor.SensorType} | {sensor.Name} | {value}");
        }

        foreach (var sub in hardware.SubHardware)
        {
            DumpHardware(lines, sub, depth + 1);
        }
    }

    private static void UpdateHardwareTree(IHardware hardware)
    {
        hardware.Update();
        foreach (IHardware subHardware in hardware.SubHardware)
        {
            UpdateHardwareTree(subHardware);
        }
    }

    private static IEnumerable<ISensor> EnumerateSensors(IHardware hardware)
    {
        foreach (var sensor in hardware.Sensors)
        {
            yield return sensor;
        }

        foreach (var subHardware in hardware.SubHardware)
        {
            foreach (var sensor in EnumerateSensors(subHardware))
            {
                yield return sensor;
            }
        }
    }

    private static float? PickUsage(IEnumerable<ISensor> sensors, bool preferTotal)
    {
        var valid = sensors
            .Where(s => s.SensorType == SensorType.Load && s.Value.HasValue)
            .Where(s => s.Value!.Value is >= 0 and <= 100)
            .ToArray();

        if (valid.Length == 0)
        {
            return null;
        }

        if (preferTotal)
        {
            var total = valid.FirstOrDefault(s =>
                s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase)
                || s.Name.Equals("CPU Total", StringComparison.OrdinalIgnoreCase)
                || s.Name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase)
                || s.Name.Contains("D3D", StringComparison.OrdinalIgnoreCase));
            if (total?.Value is not null)
            {
                return ClampPercent(total.Value.Value);
            }
        }

        return ClampPercent(valid.Average(s => s.Value!.Value));
    }

    private static float? PickTemperature(IEnumerable<ISensor> sensors, bool cpu)
    {
        float minPlausible = 15f;
        float maxPlausible = cpu ? 115f : 125f;

        var valid = sensors
            .Where(s => s.SensorType == SensorType.Temperature && s.Value.HasValue)
            .Where(s => IsPlausibleTemperature(s.Value!.Value, minPlausible, maxPlausible))
            .ToArray();

        if (valid.Length == 0)
        {
            return null;
        }

        string[] preferred = cpu
            ? ["Package", "Tctl", "Tdie", "CCD", "Core Max", "CPU Core", "CPU"]
            : ["GPU Core", "Core", "Hot Spot", "Junction", "GPU"];

        foreach (var key in preferred)
        {
            var sensor = valid.FirstOrDefault(s => s.Name.Contains(key, StringComparison.OrdinalIgnoreCase));
            if (sensor?.Value is not null)
            {
                return sensor.Value.Value;
            }
        }

        // 多个核心温度时，显示最高值更接近小飞机/硬件监控软件的直觉，也能避免平均值被异常低值拖低。
        return valid.Max(s => s.Value!.Value);
    }

    private static bool IsPlausibleTemperature(float value, float min, float max)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return false;
        }

        return value >= min && value <= max;
    }

    private static float ClampPercent(float value) => Math.Clamp(value, 0, 100);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _computer.Close();
        _disposed = true;
    }
}
