using System.IO;
using System.Threading.Tasks;
using TelemetryAgent.Core.Mock;
using TelemetryAgent.Core.Setups;
using Xunit;

namespace TelemetryAgent.Tests;

public class SampleDataGenerationTest
{
    [Fact]
    public async Task GenerateDefaultSampleData()
    {
        string sampleDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../sample_data"));
        if (!Directory.Exists(sampleDir))
        {
            Directory.CreateDirectory(sampleDir);
        }

        string ndjsonPath = Path.Combine(sampleDir, "spa_gt3_benchmark.ndjson");
        await BenchmarkDatasetGenerator.GenerateBenchmarkNdjsonAsync(ndjsonPath, frameCount: 1200);

        string setupPath = Path.Combine(sampleDir, "amg_gt3_evo_spa_baseline.json");
        string setupContent = SetupSampleGenerator.GenerateAccSetupJson("amg_gt3_evo", frontArb: 4, rearArb: 2);
        await File.WriteAllTextAsync(setupPath, setupContent);

        Assert.True(File.Exists(ndjsonPath));
        Assert.True(File.Exists(setupPath));
    }
}
