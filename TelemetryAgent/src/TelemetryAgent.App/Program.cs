using Avalonia;
using Avalonia.Win32;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TelemetryAgent.Core.Mock;
using TelemetryAgent.Core.Models;
using TelemetryAgent.Core.Services;

namespace TelemetryAgent.App;

class Program
{
    [STAThread]
    public static async Task Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "telemetry_agent_crash.log");
                File.AppendAllText(logPath, $"[CRASH {DateTime.UtcNow:O}]\n{e.ExceptionObject}\n\n");
            }
            catch { }
        };

        // Support headless/CLI execution for CI, Docker, or Wine background services
        if (args.Length > 0 && (args[0] == "--headless" || args[0] == "--cli" || args[0] == "--mock"))
        {
            await RunHeadlessAsync(args);
            return;
        }

        bool forceSoftware = args.Any(a => a.Equals("--software", StringComparison.OrdinalIgnoreCase) ||
                                          a.Equals("--software-render", StringComparison.OrdinalIgnoreCase))
                             || Environment.GetEnvironmentVariable("AVALONIA_SOFTWARE") == "1";

        if (forceSoftware)
        {
            BuildAvaloniaApp(forceSoftware: true).StartWithClassicDesktopLifetime(args);
            return;
        }

        try
        {
            // Attempt standard platform detection with hardware backends
            BuildAvaloniaApp(forceSoftware: false).StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            // Automatic fallback for Wine / Proton / FEX-Emu when GPU/D3D driver initialization fails
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "telemetry_agent_crash.log");
                File.AppendAllText(logPath, $"[INFO] GPU acceleration failed ({ex.Message}). Retrying with Skia Software Rasterizer under Wine/FEX...\n");

                BuildAvaloniaApp(forceSoftware: true).StartWithClassicDesktopLifetime(args);
            }
            catch (Exception fatal)
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "telemetry_agent_crash.log");
                File.AppendAllText(logPath, $"[FATAL CRASH]: {fatal}\n");
                throw;
            }
        }
    }

    public static AppBuilder BuildAvaloniaApp(bool forceSoftware = false)
    {
        var renderingModes = forceSoftware
            ? new[] { Win32RenderingMode.Software }
            : new[]
            {
                Win32RenderingMode.AngleEgl,
                Win32RenderingMode.Wgl,
                Win32RenderingMode.Vulkan,
                Win32RenderingMode.Software
            };

        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions
            {
                RenderingMode = renderingModes,
                CompositionMode = new[]
                {
                    Win32CompositionMode.RedirectionSurface,
                    Win32CompositionMode.LowLatencyDxgiSwapChain,
                    Win32CompositionMode.WinUIComposition
                }
            })
            .WithInterFont()
            .LogToTrace();
    }

    private static async Task RunHeadlessAsync(string[] args)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(@"============================================================");
        Console.WriteLine(@"  NOSTRATEGIA TELEMETRY EDGE AGENT (Headless Mode)");
        Console.WriteLine(@"============================================================");
        Console.ResetColor();

        bool useMock = args.Length > 0 && args[0] == "--mock";
        var config = new AgentConfiguration
        {
            DataSource = useMock ? DataSourceType.MockEngine : DataSourceType.LiveMemory,
            TargetFrequencyHz = 60,
            EnableAdaptiveThrottling = true
        };

        Console.WriteLine($"[INIT] Data Source: {config.DataSource}");
        Console.WriteLine($"[INIT] Ingestion URL: {config.IngestionUrl}");
        Console.WriteLine($"[INIT] Target Rate: {config.TargetFrequencyHz} Hz (Adaptive Throttling: {config.EnableAdaptiveThrottling})");

        await using var coordinator = new TelemetryAgentCoordinator(config);

        coordinator.StatusChanged += status =>
        {
            Console.WriteLine($"[STATUS] State: {status.State} | Game: {status.GameLink} | Backend: {status.BackendLink} | Effective Hz: {status.CurrentHz} | Saved: {status.BandwidthSavingsPercentage}%");
        };

        await coordinator.StartAsync();
        Console.WriteLine(@"[AGENT] Agent started. Press Ctrl+C to stop.");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            await Task.Delay(Timeout.Infinite, cts.Token);
        }
        catch (OperationCanceledException) { }

        Console.WriteLine(@"[AGENT] Stopping agent...");
        await coordinator.StopAsync();
        Console.WriteLine(@"[AGENT] Stopped cleanly.");
    }
}
