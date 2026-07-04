namespace TinyFpsOverlay.Models;

public sealed class MetricsSnapshot
{
    public double? Fps { get; init; }
    public double? CpuUsage { get; init; }
    public double? CpuTemperature { get; init; }
    public double? GpuUsage { get; init; }
    public double? GpuTemperature { get; init; }
    public string? GpuName { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.Now;
}
