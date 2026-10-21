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
}
