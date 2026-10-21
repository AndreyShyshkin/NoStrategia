using Avalonia;
using System;
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
        // Support headless/CLI execution for CI, Docker, or Wine background services
        if (args.Length > 0 && (args[0] == "--headless" || args[0] == "--cli" || args[0] == "--mock"))
        {
            await RunHeadlessAsync(args);
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

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
