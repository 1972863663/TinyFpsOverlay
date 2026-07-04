namespace TinyFpsOverlay.Services.CpuTemperature;

public sealed class RyzenSmuTemperatureProvider : ICpuTemperatureProvider
{
    public string Name => "Ryzen SMU";

    // Placeholder until a maintained, Zen 5 / Ryzen 9000-compatible,
    // license-compatible SMU backend is selected and verified.
    public bool IsAvailable => false;

    public double? ReadCpuTemperature() => null;
}
