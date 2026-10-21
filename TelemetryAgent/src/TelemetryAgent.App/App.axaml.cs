using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace TelemetryAgent.App;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private TrayIcon? _trayIcon;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _mainWindow = new MainWindow();
            desktop.MainWindow = _mainWindow;

            SetupTrayIcon(_mainWindow, desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetupTrayIcon(MainWindow mainWindow, IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            _trayIcon = new TrayIcon
            {
                ToolTipText = "NoStrategia Telemetry Agent (Active)",
                IsVisible = true
            };

            var menu = new NativeMenu();

            var openItem = new NativeMenuItem("Open Dashboard");
            openItem.Click += (s, e) => mainWindow.RestoreWindow();
            menu.Add(openItem);

            menu.Add(new NativeMenuItemSeparator());

            var exitItem = new NativeMenuItem("Exit Agent");
            exitItem.Click += (s, e) =>
            {
                mainWindow.ForceExit();
                desktop.Shutdown();
            };
            menu.Add(exitItem);

            _trayIcon.Menu = menu;
            _trayIcon.Clicked += (s, e) => mainWindow.RestoreWindow();

            var trayIcons = new TrayIcons { _trayIcon };
            TrayIcon.SetIcons(this, trayIcons);
        }
        catch
        {
            // Fallback gracefully on platforms without system tray support
        }
    }
}
