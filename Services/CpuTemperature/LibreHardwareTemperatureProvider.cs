namespace TinyFpsOverlay.Services.CpuTemperature;

public sealed class LibreHardwareTemperatureProvider : ICpuTemperatureProvider
{
    private readonly Func<double?> _readLatestCpuTemperature;

    public LibreHardwareTemperatureProvider(Func<double?> readLatestCpuTemperature)
    {
        _readLatestCpuTemperature = readLatestCpuTemperature;
    }

    public string Name => "LibreHardwareMonitor";

    // LibreHardwareMonitor itself is already opened by HardwareMonitorService.
    // Availability is judged by whether the latest hardware scan produced a plausible CPU temperature.
    public bool IsAvailable => TemperatureValidation.IsValidCpuTemperature(_readLatestCpuTemperature());

    public double? ReadCpuTemperature() => _readLatestCpuTemperature();
}
