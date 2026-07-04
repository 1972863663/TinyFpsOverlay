namespace TinyFpsOverlay.Services.CpuTemperature;

internal static class TemperatureValidation
{
    public static bool IsValidCpuTemperature(double? value)
    {
        if (!value.HasValue)
        {
            return false;
        }

        double v = value.Value;
        return !double.IsNaN(v) && !double.IsInfinity(v) && v is >= 15 and <= 115;
    }
}
