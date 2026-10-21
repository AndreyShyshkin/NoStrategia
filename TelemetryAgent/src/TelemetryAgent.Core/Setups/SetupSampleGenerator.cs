using System.Text.Json;

namespace TelemetryAgent.Core.Setups;

public static class SetupSampleGenerator
{
    public static string GenerateAccSetupJson(
        string carName = "amg_gt3_evo",
        int frontArb = 4,
        int rearArb = 2,
        float[]? tyrePressures = null)
    {
        tyrePressures ??= [58, 60, 56, 58];

        var setupObj = new
        {
            carName,
            basicSetup = new
            {
                tyres = new
                {
                    tyreCompound = 0,
                    tyrePressure = tyrePressures
                },
                alignment = new
                {
                    camber = new[] { 34, 34, 28, 28 },
                    toe = new[] { 12, 12, 18, 18 }
                },
                electronics = new
                {
                    tC1 = 3,
                    tC2 = 4,
                    abs = 3,
                    eCUMap = 1
                },
                strategy = new
                {
                    fuel = 62,
                    tyreSet = 1,
                    pitStrategy = new[]
                    {
                        new
                        {
                            fuelToAdd = 45,
                            tyres = new { tyreCompound = 0, tyrePressure = new[] { 59, 61, 57, 59 } },
                            tyreSet = 2
                        }
                    }
                }
            },
            advancedSetup = new
            {
                mechanicalBalance = new
                {
                    aRBFront = frontArb,
                    aRBRear = rearArb,
                    wheelRate = new[] { 14, 14, 11, 11 }
                },
                dampers = new
                {
                    bumpSlow = new[] { 10, 10, 8, 8 },
                    bumpFast = new[] { 15, 15, 12, 12 },
                    reboundSlow = new[] { 12, 12, 10, 10 },
                    reboundFast = new[] { 18, 18, 14, 14 }
                },
                aeroBalance = new
                {
                    rideHeight = new[] { 14, 28 },
                    rearWing = 8,
                    brakeDuct = new[] { 3, 2 }
                },
                drivetrain = new
                {
                    preload = 6
                }
            }
        };

        return JsonSerializer.Serialize(setupObj, new JsonSerializerOptions { WriteIndented = true });
    }

    public static async Task<string> CreateTestSetupFileAsync(
        string targetDirectory,
        string carName = "amg_gt3_evo",
        string track = "spa",
        string setupName = "qualifying_q1.json",
        int frontArb = 4,
        int rearArb = 2)
    {
        string carTrackDir = Path.Combine(targetDirectory, carName, track);
        if (!Directory.Exists(carTrackDir))
        {
            Directory.CreateDirectory(carTrackDir);
        }

        string fullPath = Path.Combine(carTrackDir, setupName);
        string json = GenerateAccSetupJson(carName, frontArb, rearArb);
        await File.WriteAllTextAsync(fullPath, json);
        return fullPath;
    }
}
