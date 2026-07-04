using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace TinyFpsOverlay.Services;

public sealed class PresentMonFpsService : IDisposable
{
    private readonly object _lock = new();
    private readonly Queue<double> _recentFps = new();
    private readonly string? _presentMonPath;
    private Process? _process;
    private int? _trackedPid;
    private DateTime _lastTargetCheck = DateTime.MinValue;
    private bool _disposed;

    public PresentMonFpsService()
    {
        _presentMonPath = FindPresentMon();
    }

    public double? CurrentFps
    {
        get
        {
            lock (_lock)
            {
                return _recentFps.Count == 0 ? null : _recentFps.Average();
            }
        }
    }

    public string Status
    {
        get
        {
            if (_presentMonPath is null)
            {
                return "未找到 PresentMon";
            }

            if (_trackedPid is null)
            {
                return "等待游戏窗口";
            }

            return _process is { HasExited: false } ? $"PID {_trackedPid}" : "FPS 采集中断";
        }
    }

    public void Tick()
    {
        if (_disposed || _presentMonPath is null)
        {
            return;
        }

        if ((DateTime.Now - _lastTargetCheck).TotalSeconds < 2)
        {
            return;
        }

        _lastTargetCheck = DateTime.Now;
        int? foregroundPid = ForegroundProcessHelper.GetForegroundProcessId();
        if (foregroundPid is null || foregroundPid == Environment.ProcessId)
        {
            return;
        }

        string? name = GetProcessName(foregroundPid.Value);
        if (name is null || IsIgnoredProcess(name))
        {
            return;
        }

        if (_trackedPid == foregroundPid && _process is { HasExited: false })
        {
            return;
        }

        StartForProcess(foregroundPid.Value);
    }

    private void StartForProcess(int pid)
    {
        StopProcess();
        lock (_lock)
        {
            _recentFps.Clear();
        }

        var psi = new ProcessStartInfo
        {
            FileName = _presentMonPath!,
            // --output_stdout 输出 CSV；--terminate_after_timed 用不到，外部进程常驻采集当前前台游戏。
            Arguments = $"--process_id {pid} --output_stdout --no_console_stats --stop_existing_session --session_name TinyFpsOverlay",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(_presentMonPath!)!
        };

        try
        {
            _process = Process.Start(psi);
            _trackedPid = pid;
            if (_process is null)
            {
                return;
            }

            _ = Task.Run(() => ReadPresentMonOutputAsync(_process));
        }
        catch
        {
            _trackedPid = null;
            _process = null;
        }
    }

    private async Task ReadPresentMonOutputAsync(Process process)
    {
        int msBetweenPresentsIndex = -1;

        try
        {
            while (!process.HasExited)
            {
                string? line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var columns = SplitCsvLine(line);
                if (columns.Length == 0)
                {
                    continue;
                }

                if (msBetweenPresentsIndex < 0)
                {
                    msBetweenPresentsIndex = Array.FindIndex(columns, c =>
                        string.Equals(c, "MsBetweenPresents", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(c, "msBetweenPresents", StringComparison.OrdinalIgnoreCase));
                    continue;
                }

                if (msBetweenPresentsIndex >= columns.Length)
                {
                    continue;
                }

                if (!double.TryParse(columns[msBetweenPresentsIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out double ms))
                {
                    continue;
                }

                if (ms <= 0.1 || ms > 1000)
                {
                    continue;
                }

                double fps = 1000.0 / ms;
                if (fps is < 1 or > 2000)
                {
                    continue;
                }

                lock (_lock)
                {
                    _recentFps.Enqueue(fps);
                    while (_recentFps.Count > 30)
                    {
                        _recentFps.Dequeue();
                    }
                }
            }
        }
        catch
        {
            // 保持悬浮窗稳定，不让采集异常把 UI 打崩。
        }
    }

    private static string? FindPresentMon()
    {
        string baseDir = AppContext.BaseDirectory;
        string[] candidates =
        [
            Path.Combine(baseDir, "tools", "PresentMon-2.5.1-x64.exe"),
            Path.Combine(baseDir, "PresentMon-2.5.1-x64.exe"),
            Path.Combine(Environment.CurrentDirectory, "tools", "PresentMon-2.5.1-x64.exe"),
            Path.Combine(Environment.CurrentDirectory, "PresentMon-2.5.1-x64.exe")
        ];

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? GetProcessName(int pid)
    {
        try
        {
            return Process.GetProcessById(pid).ProcessName;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsIgnoredProcess(string processName)
    {
        string[] ignored =
        [
            "explorer", "ShellExperienceHost", "SearchHost", "StartMenuExperienceHost",
            "TextInputHost", "ApplicationFrameHost", "SystemSettings", "Taskmgr",
            "devenv", "Code", "WindowsTerminal", "powershell", "pwsh", "cmd",
            "TinyFpsOverlay"
        ];

        return ignored.Any(x => string.Equals(x, processName, StringComparison.OrdinalIgnoreCase));
    }

    private static string[] SplitCsvLine(string line)
    {
        var values = new List<string>();
        var current = new System.Text.StringBuilder();
        bool quoted = false;

        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (ch == ',' && !quoted)
            {
                values.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        values.Add(current.ToString().Trim());
        return values.ToArray();
    }

    private void StopProcess()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // ignored
        }
        finally
        {
            _process?.Dispose();
            _process = null;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        StopProcess();
    }

    private static class ForegroundProcessHelper
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        public static int? GetForegroundProcessId()
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return null;
            }

            _ = GetWindowThreadProcessId(hwnd, out uint pid);
            return pid == 0 ? null : (int)pid;
        }
    }
}
