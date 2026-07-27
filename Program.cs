using TinyFpsOverlay;

ApplicationConfiguration.Initialize();
bool toolboxManaged = args.Any(arg =>
    string.Equals(arg, "--toolbox-managed", StringComparison.OrdinalIgnoreCase)
    || string.Equals(arg, "--no-tray", StringComparison.OrdinalIgnoreCase));
using var form = new MainForm(toolboxManaged);
Application.Run(form);
