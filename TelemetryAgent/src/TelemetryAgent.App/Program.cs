using Avalonia;
using Avalonia.Win32;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using TelemetryAgent.Core.Mock;
using TelemetryAgent.Core.Models;
using TelemetryAgent.Core.Services;

namespace TelemetryAgent.App;

class Program
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    [STAThread]
    public static void Main(string[] args)
    {
        string exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppDomain.CurrentDomain.BaseDirectory;
        string bootLog = Path.Combine(exeDir, "telemetry_agent_boot.log");
        string crashLog = Path.Combine(exeDir, "telemetry_agent_crash.log");

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            try
            {
                File.AppendAllText(crashLog, $"[CRASH {DateTime.UtcNow:O}]\n{e.ExceptionObject}\n\n");
            }
            catch { }
        };

        // Support headless/CLI execution for CI, Docker, or Wine background services
        if (args.Length > 0 && (args[0] == "--headless" || args[0] == "--cli" || args[0] == "--mock"))
        {
            RunHeadlessAsync(args).GetAwaiter().GetResult();
            return;
        }

        bool isWine = IsRunningUnderWine();
        bool forceGpu = args.Any(a => a.Equals("--gpu", StringComparison.OrdinalIgnoreCase));

        // Under Wine / Proton / FEX-Emu / DXMT / Apple Silicon: ALWAYS default to GDI Software Rasterizer.
        // This is 100% immune to DXVK, DXMT, ANGLE, Metal, and FEX translation deadlocks.
        // GPU rendering can be enabled explicitly with --gpu.
        bool useSoftware = !forceGpu;

        try
        {
            File.WriteAllText(bootLog, $"[BOOT {DateTime.UtcNow:O}]\n" +
                                       $"  IsWine: {isWine}\n" +
                                       $"  UseSoftware: {useSoftware}\n" +
                                       $"  ForceGpu: {forceGpu}\n" +
                                       $"  Architecture: {RuntimeInformation.ProcessArchitecture}\n" +
                                       $"  OS: {RuntimeInformation.OSDescription}\n" +
                                       $"  ProcessPath: {Environment.ProcessPath}\n\n");
        }
        catch { }

        try
        {
            BuildAvaloniaApp(forceSoftware: useSoftware).StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            try
            {
                File.AppendAllText(crashLog, $"[STARTUP RETRY]: {ex.Message}. Retrying with Skia Software Rasterizer...\n");
                BuildAvaloniaApp(forceSoftware: true).StartWithClassicDesktopLifetime(args);
            }
            catch (Exception fatal)
            {
                File.AppendAllText(crashLog, $"[FATAL CRASH]: {fatal}\n");
                throw;
            }
        }
    }

    public static bool IsRunningUnderWine()
    {
        // 1. Check Wine/Proton environment variables
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WINELOADER")) ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WINEPREFIX")) ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WINESERVER")) ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PROTON_VERSION")) ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("STEAM_COMPAT_DATA_PATH")) ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FEX_APP_CONFIG")))
        {
            return true;
        }

        // 2. Check ntdll export wine_get_version
        if (OperatingSystem.IsWindows())
        {
            try
            {
                IntPtr hNtdll = GetModuleHandle("ntdll.dll");
                if (hNtdll != IntPtr.Zero && GetProcAddress(hNtdll, "wine_get_version") != IntPtr.Zero)
                {
                    return true;
                }
            }
            catch { }
        }

        return false;
    }

    public static AppBuilder BuildAvaloniaApp(bool forceSoftware = true)
    {
        // When forceSoftware is true, use purely Win32 GDI Software rendering.
        // This is 100% immune to DXVK, DXMT, ANGLE, Metal, and FEX translation deadlocks.
        var renderingModes = forceSoftware
            ? new[] { Win32RenderingMode.Software }
            : new[]
            {
                Win32RenderingMode.AngleEgl,
                Win32RenderingMode.Wgl,
                Win32RenderingMode.Software
            };

        var compositionModes = forceSoftware
            ? new[] { Win32CompositionMode.RedirectionSurface }
            : new[]
            {
                Win32CompositionMode.RedirectionSurface,
                Win32CompositionMode.LowLatencyDxgiSwapChain,
                Win32CompositionMode.WinUIComposition
            };

        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions
            {
                RenderingMode = renderingModes,
                CompositionMode = compositionModes
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
