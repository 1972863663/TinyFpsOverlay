namespace TinyFpsOverlay.Services.CpuTemperature;

public interface ICpuTemperatureProvider
{
    string Name { get; }
    bool IsAvailable { get; }
    double? ReadCpuTemperature();
}
