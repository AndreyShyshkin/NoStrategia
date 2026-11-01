using TelemetryAgent.Core.Setups;
using Xunit;

namespace TelemetryAgent.Tests;

public class SetupWatcherTests
{
    [Fact]
    public void Sha256_ComputesDeterministicHash()
    {
        string json = SetupSampleGenerator.GenerateAccSetupJson("amg_gt3_evo", frontArb: 4, rearArb: 2);
        string hash1 = SetupWatcherService.ComputeSha256(json);
        string hash2 = SetupWatcherService.ComputeSha256(json);

        Assert.Equal(hash1, hash2);
        Assert.Equal(64, hash1.Length); // 256 bits = 64 hex characters
    }

    [Fact]
    public async Task SetupWatcher_ProcessesFileAndExtractsMetadata()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "NoStrategia_TestSetups_" + Guid.NewGuid().ToString("N"));
        try
        {
            var watcher = new SetupWatcherService(tempDir);
            string setupPath = await SetupSampleGenerator.CreateTestSetupFileAsync(
                tempDir,
                carName: "amg_gt3_evo",
                track: "spa",
                setupName: "race_q1.json",
                frontArb: 4,
                rearArb: 2);

            var payload = await watcher.ProcessSetupFileAsync(setupPath);

            Assert.NotNull(payload);
            Assert.Equal("race_q1.json", payload.FileName);
            Assert.Equal("amg_gt3_evo", payload.CarModel);
            Assert.Equal("spa", payload.Track);
            Assert.False(string.IsNullOrWhiteSpace(payload.Hash));

            // Verify deduplication: processing identical file immediately returns null
            var duplicate = await watcher.ProcessSetupFileAsync(setupPath);
            Assert.Null(duplicate);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SetupWatcher_DiscoversExistingActiveSetupOnScan()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "NoStrategia_TestScan_" + Guid.NewGuid().ToString("N"));
        try
        {
            // Create car/track structure similar to Wine ACC directory:
            // Setups/bentley_continental_gt3_2018/Spa/Active.json
            string setupPath = await SetupSampleGenerator.CreateTestSetupFileAsync(
                tempDir,
                carName: "bentley_continental_gt3_2018",
                track: "Spa",
                setupName: "Active.json",
                frontArb: 5,
                rearArb: 3);

            var watcher = new SetupWatcherService(tempDir);
            var scanned = await watcher.ScanExistingSetupsAsync();

            Assert.NotNull(scanned);
            Assert.Equal("Active.json", scanned.FileName);
            Assert.Equal("bentley_continental_gt3_2018", scanned.CarModel);
            Assert.Equal("Spa", scanned.Track);

            // Test Query by Car & Track
            var queried = await watcher.TryLoadSetupForCarAndTrackAsync("bentley_continental_gt3_2018", "Spa");
            Assert.NotNull(queried);
            Assert.Equal("Active.json", queried.FileName);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SetupWatcher_DoesNotSyncInLobbyOrUnknownCar()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "NoStrategia_TestLobby_" + Guid.NewGuid().ToString("N"));
        try
        {
            await SetupSampleGenerator.CreateTestSetupFileAsync(
                tempDir,
                carName: "bentley_continental_gt3_2018",
                track: "Spa",
                setupName: "Bentley_SPA_Q.json");

            var watcher = new SetupWatcherService(tempDir);

            // Empty car / lobby names must NOT load any setup
            var emptyResult = await watcher.TryLoadSetupForCarAndTrackAsync("", "");
            Assert.Null(emptyResult);

            var lobbyResult = await watcher.TryLoadSetupForCarAndTrackAsync("Assetto Corsa Competizione", "Main Menu / Lobby");
            Assert.Null(lobbyResult);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SetupWatcher_MatchesActiveSetupFromLiveTelemetry()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "NoStrategia_TestMatch_" + Guid.NewGuid().ToString("N"));
        try
        {
            string trackDir = Path.Combine(tempDir, "bentley_continental_gt3_2018", "Spa");
            Directory.CreateDirectory(trackDir);

            // Setup 1: Quali (Fuel 17, TC 2)
            string qJson = """
            {
                "carName": "bentley_continental_gt3_2018",
                "basicSetup": {
                    "strategy": { "fuel": 17 },
                    "electronics": { "tC1": 2, "abs": 2, "eCUMap": 0 }
                }
            }
            """;
            await File.WriteAllTextAsync(Path.Combine(trackDir, "Bentley_SPA_Q.json"), qJson);

            // Setup 2: Race (Fuel 55, TC 4)
            string rJson = """
            {
                "carName": "bentley_continental_gt3_2018",
                "basicSetup": {
                    "strategy": { "fuel": 55 },
                    "electronics": { "tC1": 4, "abs": 2, "eCUMap": 0 }
                }
            }
            """;
            await File.WriteAllTextAsync(Path.Combine(trackDir, "Bentley_SPA_R.json"), rJson);

            var watcher = new SetupWatcherService(tempDir);

            // When user loaded Q setup into car
            var liveQ = new LiveCarSetupContext(Fuel: 17.0f, Tc: 2, Abs: 2, EngineMap: 0, InGarage: true);
            var resultQ = await watcher.TryLoadSetupForCarAndTrackAsync("bentley_continental_gt3_2018", "Spa", liveQ);
            Assert.NotNull(resultQ);
            Assert.Equal("Bentley_SPA_Q.json", resultQ.FileName);
            Assert.True(resultQ.IsLiveMatched);

            // When user in garage loads R setup into car
            var liveR = new LiveCarSetupContext(Fuel: 55.0f, Tc: 4, Abs: 2, EngineMap: 0, InGarage: true);
            var resultR = await watcher.TryLoadSetupForCarAndTrackAsync("bentley_continental_gt3_2018", "Spa", liveR);
            Assert.NotNull(resultR);
            Assert.Equal("Bentley_SPA_R.json", resultR.FileName);
            Assert.True(resultR.IsLiveMatched);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }
}
