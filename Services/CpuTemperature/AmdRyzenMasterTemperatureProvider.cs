using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TinyFpsOverlay.Services.CpuTemperature;

public sealed class AmdRyzenMasterTemperatureProvider : ICpuTemperatureProvider
{
    private const string RegistryPath = @"SOFTWARE\AMD\RyzenMasterMonitoringSDK";
    private const string InstallationPathValue = "InstallationPath";
    private const int DtCpu = 0;

    // x64 MSVC C++ interface vtable slots, verified against AMD Ryzen Master
    // Monitoring SDK 3.0.1.4971 headers and official sample source:
    // IPlatform::Init/UnInit/GetIDeviceManager => 0/1/2
    // IDeviceManager::GetDevice(AOD_DEVICE_TYPE,unsigned long) => 2
    // ICPUEx::GetCPUParameters(CPUParameters&) => 17
    private const int PlatformInitSlot = 0;
    private const int PlatformUnInitSlot = 1;
    private const int PlatformGetDeviceManagerSlot = 2;
    private const int DeviceManagerGetDeviceByTypeSlot = 2;
    private const int CpuGetCpuParametersSlot = 17;

    private static readonly TimeSpan MinimumReadInterval = TimeSpan.FromSeconds(1);

    private readonly object _sync = new();

    private bool _initializationAttempted;
    private bool _available;
    private string? _sdkPath;
    private nint _platformLibrary;
    private nint _platform;
    private nint _cpu;
    private PlatformUnInitDelegate? _platformUnInit;
    private GetCpuParametersDelegate? _getCpuParameters;

    private DateTime _lastReadAttemptUtc = DateTime.MinValue;
    private double? _lastValidTemperature;

    public string Name => "AMD Ryzen Master Monitoring SDK";

    public bool IsAvailable
    {
        get
        {
            lock (_sync)
            {
                return EnsureInitialized();
            }
        }
    }

    public double? ReadCpuTemperature()
    {
        lock (_sync)
        {
            if (!EnsureInitialized() || _getCpuParameters is null || _cpu == 0)
            {
                return null;
            }

            var now = DateTime.UtcNow;
            if (now - _lastReadAttemptUtc < MinimumReadInterval)
            {
                return _lastValidTemperature;
            }

            _lastReadAttemptUtc = now;

            try
            {
                var parameters = new CpuParameters();
                var result = _getCpuParameters(_cpu, ref parameters);
                if (result != 0)
                {
                    return null;
                }

                var temperature = parameters.Temperature;
                if (!TemperatureValidation.IsValidCpuTemperature(temperature))
                {
                    return null;
                }

                _lastValidTemperature = temperature;
                return temperature;
            }
            catch
            {
                return null;
            }
        }
    }

    ~AmdRyzenMasterTemperatureProvider()
    {
        try
        {
            if (_platform != 0)
            {
                _platformUnInit?.Invoke(_platform);
            }

            if (_platformLibrary != 0)
            {
                FreeLibrary(_platformLibrary);
            }
        }
        catch
        {
            // Native cleanup must never escape the finalizer thread.
        }
    }

    private bool EnsureInitialized()
    {
        if (_initializationAttempted)
        {
            return _available;
        }

        _initializationAttempted = true;

        try
        {
            _sdkPath = ReadSdkPathFromRegistry();
            if (string.IsNullOrWhiteSpace(_sdkPath))
            {
                return false;
            }

            var platformPath = Path.Combine(_sdkPath, "bin", "Platform.dll");
            if (!File.Exists(platformPath))
            {
                return false;
            }

            _platformLibrary = LoadLibraryEx(platformPath, 0, LoadLibrarySearchDllLoadDir | LoadLibrarySearchDefaultDirs);
            if (_platformLibrary == 0)
            {
                return false;
            }

            if (!NativeLibrary.TryGetExport(_platformLibrary, "GetPlatform", out var getPlatformAddress))
            {
                return false;
            }

            var getPlatform = Marshal.GetDelegateForFunctionPointer<GetPlatformDelegate>(getPlatformAddress);
            _platform = getPlatform();
            if (_platform == 0)
            {
                return false;
            }

            var init = GetVTableDelegate<PlatformInitDelegate>(_platform, PlatformInitSlot);
            _platformUnInit = GetVTableDelegate<PlatformUnInitDelegate>(_platform, PlatformUnInitSlot);
            var getDeviceManager = GetVTableDelegate<GetDeviceManagerDelegate>(_platform, PlatformGetDeviceManagerSlot);

            // bUseCPUOnly=true keeps initialization scoped to the CPU path used by the overlay.
            if (init(_platform, 0, true) == 0)
            {
                return false;
            }

            var deviceManager = getDeviceManager(_platform);
            if (deviceManager == 0)
            {
                return false;
            }

            var getDevice = GetVTableDelegate<GetDeviceByTypeDelegate>(deviceManager, DeviceManagerGetDeviceByTypeSlot);
            _cpu = getDevice(deviceManager, DtCpu, 0);
            if (_cpu == 0)
            {
                return false;
            }

            _getCpuParameters = GetVTableDelegate<GetCpuParametersDelegate>(_cpu, CpuGetCpuParametersSlot);
            _available = _getCpuParameters is not null;
            return _available;
        }
        catch
        {
            _available = false;
            return false;
        }
    }

    private static string? ReadSdkPathFromRegistry()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RegistryPath);
            return key?.GetValue(InstallationPathValue) as string;
        }
        catch
        {
            return null;
        }
    }

    private static T GetVTableDelegate<T>(nint nativeObject, int slot) where T : Delegate
    {
        var vtable = Marshal.ReadIntPtr(nativeObject);
        var function = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<T>(function);
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadLibraryEx(string lpFileName, nint hFile, int dwFlags);

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool FreeLibrary(nint hModule);

    private const int LoadLibrarySearchDllLoadDir = 0x00000100;
    private const int LoadLibrarySearchDefaultDirs = 0x00001000;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate nint GetPlatformDelegate();

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate byte PlatformInitDelegate(nint self, nint boardVendor, [MarshalAs(UnmanagedType.I1)] bool useCpuOnly);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate byte PlatformUnInitDelegate(nint self);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate nint GetDeviceManagerDelegate(nint self);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate nint GetDeviceByTypeDelegate(nint self, int deviceType, uint index);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate int GetCpuParametersDelegate(nint self, ref CpuParameters parameters);

    [StructLayout(LayoutKind.Sequential)]
    private struct OcMode
    {
        public uint Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EffectiveFreqData
    {
        public uint Length;
        public nint Freq;
        public nint State;
        public nint CurrentFreq;
        public nint CurrentTemp;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CpuParameters
    {
        public OcMode Mode;
        public EffectiveFreqData FreqData;
        public double PeakCoreVoltage;
        public double PeakCoreVoltage1;
        public double SocVoltage;
        public double Temperature;
        public double AverageCoreVoltage;
        public double AverageCoreVoltage1;
        public double PeakSpeed;
        public float PptLimit;
        public float PptValue;
        public float TdcLimitVdd;
        public float TdcValueVdd;
        public float TdcValueVdd1;
        public float EdcLimitVdd;
        public float EdcValueVdd;
        public float EdcValueVdd1;
        public float ChtcLimit;
        public float FclkP0Freq;
        public float CclkFmax;
        public float TdcLimitSoc;
        public float TdcValueSoc;
        public float EdcLimitSoc;
        public float EdcValueSoc;
        public float VddcrVddPower;
        public float VddcrSocPower;
        public float TdcLimitCcd;
        public float TdcValueCcd;
        public float EdcLimitCcd;
        public float EdcValueCcd;
    }
}


