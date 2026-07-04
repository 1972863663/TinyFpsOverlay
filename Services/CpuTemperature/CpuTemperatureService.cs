namespace TinyFpsOverlay.Services.CpuTemperature;

public sealed class CpuTemperatureService
{
    private readonly IReadOnlyList<ICpuTemperatureProvider> _providers;

    public CpuTemperatureService(IEnumerable<ICpuTemperatureProvider> providers)
    {
        _providers = providers.ToArray();
    }

    public double? ReadCpuTemperature()
    {
        foreach (var provider in _providers)
        {
            try
            {
                if (!provider.IsAvailable)
                {
                    continue;
                }

                var value = provider.ReadCpuTemperature();
                if (TemperatureValidation.IsValidCpuTemperature(value))
                {
                    return value;
                }
            }
            catch
            {
                // A single sensor backend must never take down the overlay.
            }
        }

        return null;
    }

    public string DescribeProviders()
    {
        return string.Join(Environment.NewLine, _providers.Select(p => $"{p.Name}: {(p.IsAvailable ? "available" : "unavailable")}"));
    }
}
