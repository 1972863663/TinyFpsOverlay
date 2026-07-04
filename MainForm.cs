using System.ComponentModel;
using System.Runtime.InteropServices;
using TinyFpsOverlay.Models;
using TinyFpsOverlay.Services;
using TinyFpsOverlay.Services.CpuTemperature;

namespace TinyFpsOverlay;

public sealed class MainForm : Form
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WmHotkey = 0x0312;
    private const int HotkeyIdShow = 1101;
    private const int HotkeyIdHide = 1102;
    private const uint VkOpenBracket = 0xDB;
    private const uint VkCloseBracket = 0xDD;

    private readonly HardwareMonitorService _hardware = new();
    private readonly PresentMonFpsService _fps = new();
    private readonly CpuTemperatureService _cpuTemperature;
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly OverlayConfig _config;
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _trayMenu;
    private readonly Label _line = new();

    private bool _reallyExit;
    private bool _dragging;
    private Point _dragStart;
    private bool _hotkeyRegistered;
    private double? _latestLibreHardwareCpuTemperature;

    public MainForm()
    {
        _config = OverlayConfigStore.Load();
        SyncStartupConfigWithRegistry();
        _cpuTemperature = new CpuTemperatureService(new ICpuTemperatureProvider[]
        {
            new AmdRyzenMasterTemperatureProvider(),
            new RyzenSmuTemperatureProvider(),
            new LibreHardwareTemperatureProvider(() => _latestLibreHardwareCpuTemperature)
        });

        Text = "TinyFpsOverlay";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(360, 22);
        BackColor = Color.Black;
        Opacity = Math.Clamp(_config.Opacity, 0.35, 1.0);
        DoubleBuffered = true;

        BuildHorizontalOverlay();
        ApplyTextColor();
        PositionTopCenterIfNeeded();

        _trayMenu = BuildTrayMenu();
        _tray = new NotifyIcon
        {
            Text = "TinyFpsOverlay",
            Icon = SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = _trayMenu
        };
        _tray.DoubleClick += (_, _) => ToggleVisible();

        _timer.Interval = 500;
        _timer.Tick += (_, _) => UpdateMetrics();
        _timer.Start();

        Move += (_, _) => SavePosition();
        FormClosing += MainForm_FormClosing;
        Shown += (_, _) =>
        {
            ApplyClickThrough(true);
            ApplyHotkeyRegistration();
            UpdateMetrics();
        };
    }

    private void BuildHorizontalOverlay()
    {
        _line.Text = "FPS --   CPU --% --°C   GPU --% --°C";
        _line.Bounds = new Rectangle(4, 0, ClientSize.Width - 8, 20);
        _line.Font = new Font("Consolas", 11.0f, FontStyle.Bold, GraphicsUnit.Point);
        _line.ForeColor = Color.FromArgb(_config.TextColorArgb);
        _line.BackColor = Color.Transparent;
        _line.TextAlign = ContentAlignment.MiddleCenter;
        _line.UseMnemonic = false;
        _line.AutoEllipsis = false;
        // Mouse input disabled: overlay is fixed and click-through.


        Controls.Add(_line);




    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        BuildSimpleTrayMenu(menu);
        return menu;
    }

    private void RefreshTrayMenu()
    {
        _tray.ContextMenuStrip = null;
        _trayMenu.Items.Clear();
        BuildSimpleTrayMenu(_trayMenu);
        _tray.ContextMenuStrip = _trayMenu;
    }

    private void BuildSimpleTrayMenu(ContextMenuStrip menu)
    {
        menu.Items.Add("热键：" + (_config.HotkeyEnabled ? "开启  [显示  ]隐藏" : "关闭"), null, (_, _) => ToggleHotkeyEnabled());
        menu.Items.Add("开机自启动：" + (_config.AutoStartEnabled ? "开启" : "关闭"), null, (_, _) => ToggleAutoStart());
        menu.Items.Add("字体颜色...", null, (_, _) => PickTextColor());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("透明度");
        menu.Items.Add(CreateOpacitySliderMenuItem());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApp());
    }

    private ToolStripControlHost CreateOpacitySliderMenuItem()
    {
        var panel = new Panel
        {
            Width = 230,
            Height = 44,
            Padding = new Padding(8, 0, 8, 0)
        };

        var leftLabel = new Label
        {
            Text = "透明",
            AutoSize = true,
            Location = new Point(6, 15)
        };

        var rightLabel = new Label
        {
            Text = "不透明",
            AutoSize = true,
            Location = new Point(180, 15)
        };

        var slider = new TrackBar
        {
            Minimum = 35,
            Maximum = 100,
            TickFrequency = 5,
            SmallChange = 1,
            LargeChange = 5,
            Value = (int)Math.Round(Math.Clamp(_config.Opacity, 0.35, 1.0) * 100),
            AutoSize = false,
            Width = 140,
            Height = 34,
            Location = new Point(42, 5)
        };

        slider.Scroll += (_, _) => SetOpacity(slider.Value / 100.0);
        slider.ValueChanged += (_, _) => SetOpacity(slider.Value / 100.0);

        panel.Controls.Add(leftLabel);
        panel.Controls.Add(slider);
        panel.Controls.Add(rightLabel);

        return new ToolStripControlHost(panel)
        {
            AutoSize = false,
            Width = 240,
            Height = 48,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(_config.LockedClickThrough ? Color.FromArgb(0, 255, 255, 255) : Color.FromArgb(55, 255, 255, 255));
        using var path = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 3);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.DrawPath(pen, path);
    }

    private static System.Drawing.Drawing2D.GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        int d = radius * 2;
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void UpdateMetrics()
    {
        _fps.Tick();
        var metrics = _hardware.Read(_fps.CurrentFps);
        _latestLibreHardwareCpuTemperature = metrics.CpuTemperature;
        string fps = FormatNumber(metrics.Fps, "0");
        string cpuUsage = FormatPercent(metrics.CpuUsage);
        string cpuTemp = FormatTemp(_cpuTemperature.ReadCpuTemperature());
        string gpuUsage = FormatPercent(metrics.GpuUsage);
        string gpuTemp = FormatTemp(metrics.GpuTemperature);

        _line.Text = $"FPS {fps}   CPU {cpuUsage} {cpuTemp}   GPU {gpuUsage} {gpuTemp}";
        ResizeToTextAndTopCenter();
        _tray.Text = TruncateTrayText($"TinyFpsOverlay | {_line.Text}");
    }

    private static string FormatNumber(double? value, string format) => value.HasValue ? value.Value.ToString(format) : "--";
    private static string FormatPercent(double? value) => value.HasValue ? $"{value.Value:0}%" : "--%";
    private static string FormatTemp(double? value) => value.HasValue ? $"{value.Value:0}°C" : "--°C";
    private static string TruncateTrayText(string text) => text.Length <= 63 ? text : text[..63];

    private void ResizeToTextAndTopCenter()
    {
        Size textSize = TextRenderer.MeasureText(_line.Text, _line.Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        int newWidth = Math.Max(260, textSize.Width + 18);
        int newHeight = Math.Max(20, textSize.Height + 2);
        if (ClientSize.Width != newWidth || ClientSize.Height != newHeight)
        {
            ClientSize = new Size(newWidth, newHeight);
            _line.Bounds = new Rectangle(4, 0, ClientSize.Width - 8, ClientSize.Height);
        }
        CenterTopAndSave();
    }
    private void PositionTopCenterIfNeeded()
    {
        if (_config.Left < 0)
        {
            CenterTopAndSave();
            return;
        }

        Location = new Point((int)_config.Left, (int)_config.Top);
    }

    private void CenterTopAndSave()
    {
        Rectangle wa = Screen.PrimaryScreen?.WorkingArea ?? Screen.GetWorkingArea(this);
        Left = wa.Left + (wa.Width - Width) / 2;
        Top = wa.Top;
        SavePosition();
    }

    private void Overlay_MouseDown(object? sender, MouseEventArgs e)
    {
        if (_config.LockedClickThrough)
        {
            return;
        }

        if (e.Button == MouseButtons.Right)
        {
            ToggleClickThrough();
            return;
        }

        if (e.Button == MouseButtons.Left)
        {
            _dragging = true;
            _dragStart = e.Location;
            Cursor = Cursors.SizeAll;
        }
    }

    private void Overlay_MouseMove(object? sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        Point screenPoint = PointToScreen(e.Location);
        Location = new Point(screenPoint.X - _dragStart.X, screenPoint.Y - _dragStart.Y);
    }

    private void Overlay_MouseUp(object? sender, MouseEventArgs e)
    {
        _dragging = false;
        Cursor = Cursors.Default;
        SavePosition();
    }

    private void ExportSensors()
    {
        try
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "TinyFpsOverlay-Sensors.txt");
            File.WriteAllText(path,
                _hardware.DumpSensors()
                + Environment.NewLine + Environment.NewLine
                + "CPU temperature providers"
                + Environment.NewLine
                + _cpuTemperature.DescribeProviders());
            _tray.ShowBalloonTip(3000, "TinyFpsOverlay", $"传感器列表已导出：{path}", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            _tray.ShowBalloonTip(3000, "TinyFpsOverlay", $"导出失败：{ex.Message}", ToolTipIcon.Error);
        }
    }

    private void ToggleVisible()
    {
        if (Visible)
        {
            Hide();
        }
        else
        {
            Show();
            TopMost = false;
            TopMost = true;
        }
    }

    private void ToggleClickThrough()
    {
        ApplyClickThrough(!_config.LockedClickThrough);
    }

    private void ApplyClickThrough(bool enabled)
    {
        _config.LockedClickThrough = enabled;
        int exStyle = GetWindowLong(Handle, GwlExStyle);
        exStyle |= WsExToolWindow;
        if (enabled)
        {
            exStyle |= WsExTransparent;
        }
        else
        {
            exStyle &= ~WsExTransparent;
        }

        _ = SetWindowLong(Handle, GwlExStyle, exStyle);
        Invalidate();
        OverlayConfigStore.Save(_config);
    }

    private void ToggleHotkeyEnabled()
    {
        _config.HotkeyEnabled = !_config.HotkeyEnabled;
        ApplyHotkeyRegistration();
        OverlayConfigStore.Save(_config);
        RefreshTrayMenu();
        _tray.ShowBalloonTip(1800, "TinyFpsOverlay", _config.HotkeyEnabled ? "热键已开启：[ 显示，] 隐藏" : "热键已关闭", ToolTipIcon.Info);
    }

    private void SyncStartupConfigWithRegistry()
    {
        bool registryEnabled = StartupService.IsEnabled();
        if (registryEnabled)
        {
            _config.AutoStartEnabled = true;
        }

        if (_config.AutoStartEnabled)
        {
            bool repaired = StartupService.EnableOrRepair();
            if (!repaired)
            {
                _config.AutoStartEnabled = false;
            }
        }

        OverlayConfigStore.Save(_config);
    }

    private void ToggleAutoStart()
    {
        if (_config.AutoStartEnabled)
        {
            bool disabled = StartupService.Disable();
            if (disabled)
            {
                _config.AutoStartEnabled = false;
                OverlayConfigStore.Save(_config);
                RefreshTrayMenu();
                _tray.ShowBalloonTip(2000, "TinyFpsOverlay", "开机自启动已关闭", ToolTipIcon.Info);
            }
            else
            {
                _tray.ShowBalloonTip(3000, "TinyFpsOverlay", "关闭开机自启动失败", ToolTipIcon.Error);
            }

            return;
        }

        bool enabled = StartupService.EnableOrRepair();
        if (enabled)
        {
            _config.AutoStartEnabled = true;
            OverlayConfigStore.Save(_config);
            RefreshTrayMenu();
            _tray.ShowBalloonTip(3000, "TinyFpsOverlay", "开机自启动已开启，并已绑定到当前 EXE 路径。以后移动程序后，重新打开一次并保持开启即可自动修正启动路径。", ToolTipIcon.Info);
        }
        else
        {
            _tray.ShowBalloonTip(3000, "TinyFpsOverlay", "开启开机自启动失败", ToolTipIcon.Error);
        }
    }

    private void ApplyHotkeyRegistration()
    {
        if (_hotkeyRegistered)
        {
            UnregisterHotKey(Handle, HotkeyIdShow);
            UnregisterHotKey(Handle, HotkeyIdHide);
            _hotkeyRegistered = false;
        }

        if (_config.HotkeyEnabled)
        {
            bool showOk = RegisterHotKey(Handle, HotkeyIdShow, 0, VkOpenBracket);
            bool hideOk = RegisterHotKey(Handle, HotkeyIdHide, 0, VkCloseBracket);
            _hotkeyRegistered = showOk || hideOk;
        }
    }

    private void PickTextColor()
    {
        using var dialog = new ColorDialog
        {
            AllowFullOpen = true,
            FullOpen = true,
            Color = Color.FromArgb(_config.TextColorArgb)
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _config.TextColorArgb = dialog.Color.ToArgb();
            ApplyTextColor();
            OverlayConfigStore.Save(_config);
        }
    }

    private void ApplyTextColor()
    {
        _line.ForeColor = Color.FromArgb(_config.TextColorArgb);
    }

    private void AdjustOpacity(double delta)
    {
        SetOpacity(_config.Opacity + delta);
    }

    private void SetOpacity(double value)
    {
        _config.Opacity = Math.Clamp(value, 0.35, 1.0);
        Opacity = _config.Opacity;
        OverlayConfigStore.Save(_config);
    }

    private void SavePosition()
    {
        _config.Left = Left;
        _config.Top = Top;
        OverlayConfigStore.Save(_config);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey)
        {
            int id = m.WParam.ToInt32();
            if (id == HotkeyIdShow)
            {
                if (!Visible)
                {
                    Show();
                }
                TopMost = false;
                TopMost = true;
                return;
            }

            if (id == HotkeyIdHide)
            {
                if (Visible)
                {
                    Hide();
                }
                return;
            }
        }

        base.WndProc(ref m);
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_reallyExit && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void ExitApp()
    {
        _reallyExit = true;
        _timer.Stop();
        if (_hotkeyRegistered)
        {
            UnregisterHotKey(Handle, HotkeyIdShow);
            UnregisterHotKey(Handle, HotkeyIdHide);
            _hotkeyRegistered = false;
        }
        _tray.Visible = false;
        _tray.Dispose();
        _trayMenu.Dispose();
        _fps.Dispose();
        _hardware.Dispose();
        OverlayConfigStore.Save(_config);
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_hotkeyRegistered && IsHandleCreated)
            {
                UnregisterHotKey(Handle, HotkeyIdShow);
            UnregisterHotKey(Handle, HotkeyIdHide);
                _hotkeyRegistered = false;
            }
            _timer.Dispose();
            _tray.Dispose();
            _trayMenu.Dispose();
            _fps.Dispose();
            _hardware.Dispose();
        }

        base.Dispose(disposing);
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}







