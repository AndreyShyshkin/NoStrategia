using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TelemetryAgent.Core.Models;

namespace TelemetryAgent.Core.Setups;

/// <summary>
/// Monitors ACC vehicle setup JSON files, computes cryptographic SHA-256 hashes,
/// deduplicates rapid I/O write bursts, and dispatches change events to the ingestion pipeline.
/// Features intelligent multi-path detection for Wine/CrossOver/Proton environments.
/// </summary>
public sealed class SetupWatcherService : ISetupWatcher
{
    private FileSystemWatcher? _watcher;
    private Timer? _pollTimer;
    private string _watchDirectory;
    private readonly Dictionary<string, string> _fileHashes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _fileTimestamps = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private bool _isActive;

    public bool IsActive => _isActive;
    public string WatchDirectory => _watchDirectory;
    public string LastSyncedFile { get; private set; } = string.Empty;
    public string LastSyncedHash { get; private set; } = string.Empty;
    public DateTime? LastSyncedTime { get; private set; }

    public event Action<SetupPayload>? SetupDetected;

    public SetupWatcherService(string? customDirectory = null)
    {
        _watchDirectory = !string.IsNullOrWhiteSpace(customDirectory) && Directory.Exists(customDirectory)
            ? customDirectory
            : FindAccSetupsDirectory();
    }

    /// <summary>
    /// Intelligent auto-discovery of ACC Setups folder across Windows and Wine/Proton/CrossOver bottles.
    /// Returns all existing valid setup directory candidates.
    /// </summary>
    public static List<string> FindAccSetupsCandidates()
    {
        var candidates = new List<string>();

        string[] commonUsers = ["steamuser", "crossover", "wineuser", "default", "Public"];
        string[] localDrives = ["C:\\", "D:\\", "E:\\"];

        foreach (var drive in localDrives)
        {
            string usersDir = Path.Combine(drive, "users");
            if (Directory.Exists(usersDir))
            {
                try
                {
                    foreach (var userDir in Directory.GetDirectories(usersDir))
                    {
                        candidates.Add(Path.Combine(userDir, "Documents", "Assetto Corsa Competizione", "Setups"));
                        candidates.Add(Path.Combine(userDir, "My Documents", "Assetto Corsa Competizione", "Setups"));
                    }
                }
                catch { }
            }

            foreach (var u in commonUsers)
            {
                candidates.Add(Path.Combine(drive, "users", u, "Documents", "Assetto Corsa Competizione", "Setups"));
            }
        }

        try
        {
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrWhiteSpace(docs))
                candidates.Add(Path.Combine(docs, "Assetto Corsa Competizione", "Setups"));
        }
        catch { }

        try
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userProfile))
                candidates.Add(Path.Combine(userProfile, "Documents", "Assetto Corsa Competizione", "Setups"));
        }
        catch { }

        string zUsers = Path.Combine("Z:\\", "users");
        if (Directory.Exists(zUsers))
        {
            try
            {
                foreach (var userDir in Directory.GetDirectories(zUsers))
                {
                    candidates.Add(Path.Combine(userDir, "Documents", "Assetto Corsa Competizione", "Setups"));
                }
            }
            catch { }
        }

        return candidates
            .Where(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Selects the primary ACC setups folder from available candidates.
    /// </summary>
    public static string FindAccSetupsDirectory()
    {
        var candidates = FindAccSetupsCandidates();
        string? bestCandidate = null;
        DateTime bestTimestampUtc = DateTime.MinValue;
        bool bestHasActive = false;
        string? firstExisting = null;

        foreach (var path in candidates)
        {
            try
            {
                firstExisting ??= path;

                var files = new DirectoryInfo(path).GetFiles("*.json", SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    bool hasActive = files.Any(f => f.Name.Equals("Active.json", StringComparison.OrdinalIgnoreCase));
                    DateTime newest = files.Max(f => f.LastWriteTimeUtc);
                    bool isCDrive = path.StartsWith("C:", StringComparison.OrdinalIgnoreCase);

                    if (bestCandidate == null ||
                        (hasActive && !bestHasActive) ||
                        (hasActive == bestHasActive && isCDrive && !bestCandidate.StartsWith("C:", StringComparison.OrdinalIgnoreCase)) ||
                        (hasActive == bestHasActive && newest > bestTimestampUtc))
                    {
                        bestCandidate = path;
                        bestTimestampUtc = newest;
                        bestHasActive = hasActive;
                    }
                }
            }
            catch { }
        }

        if (bestCandidate != null) return bestCandidate;
        if (firstExisting != null) return firstExisting;

        return Path.Combine("C:\\", "users", "steamuser", "Documents", "Assetto Corsa Competizione", "Setups");
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_isActive) return;

            try
            {
                if (!Directory.Exists(_watchDirectory))
                {
                    _watchDirectory = FindAccSetupsDirectory();
                }

                if (!Directory.Exists(_watchDirectory))
                {
                    Directory.CreateDirectory(_watchDirectory);
                }

                _watcher = new FileSystemWatcher(_watchDirectory, "*.json")
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                    EnableRaisingEvents = true
                };

                _watcher.Changed += OnFileEvent;
                _watcher.Created += OnFileEvent;
                _isActive = true;
            }
            catch (Exception)
            {
                _isActive = false;
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_isActive) return;

            if (_pollTimer != null)
            {
                _pollTimer.Dispose();
                _pollTimer = null;
            }

            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Changed -= OnFileEvent;
                _watcher.Created -= OnFileEvent;
                _watcher.Dispose();
                _watcher = null;
            }

            _isActive = false;
        }
    }

    public void SetDirectory(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath)) return;

        bool wasActive = _isActive;
        Stop();
        _watchDirectory = directoryPath;
        if (wasActive) Start();
    }

    public async Task<SetupPayload?> ScanExistingSetupsAsync(bool force = false, CancellationToken ct = default)
    {
        if (!Directory.Exists(_watchDirectory))
        {
            _watchDirectory = FindAccSetupsDirectory();
            if (!Directory.Exists(_watchDirectory)) return null;
        }

        try
        {
            var files = new DirectoryInfo(_watchDirectory)
                .GetFiles("*.json", SearchOption.AllDirectories)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

            if (files.Count == 0) return null;

            // Prefer Active.json if recently saved, or newest setup file
            var newest = files[0];
            var active = files.FirstOrDefault(f => f.Name.Equals("Active.json", StringComparison.OrdinalIgnoreCase));
            var targetFile = (active != null && (newest.FullName.Equals(active.FullName, StringComparison.OrdinalIgnoreCase) || Math.Abs((newest.LastWriteTimeUtc - active.LastWriteTimeUtc).TotalSeconds) <= 3))
                ? active
                : newest;

            return await ProcessSetupFileAsync(targetFile.FullName, force: force, ct);
        }
        catch
        {
            return null;
        }
    }

    public async Task<SetupPayload?> TryLoadSetupForCarAndTrackAsync(
        string carModel,
        string track,
        LiveCarSetupContext? liveContext = null,
        bool force = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(carModel) ||
            carModel.Equals("Assetto Corsa Competizione", StringComparison.OrdinalIgnoreCase) ||
            carModel.Contains("Lobby", StringComparison.OrdinalIgnoreCase))
        {
            // Do NOT scan or return any setup when car is unknown or in lobby
            return null;
        }

        try
        {
            var candidateRoots = new List<string> { _watchDirectory };
            candidateRoots.AddRange(FindAccSetupsCandidates());

            DirectoryInfo? targetSearchDir = null;
            string normCar = carModel.Replace("_", "").Replace(" ", "").ToLowerInvariant();

            foreach (var root in candidateRoots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(root)) continue;
                var rootDir = new DirectoryInfo(root);

                var carDir = rootDir.GetDirectories()
                    .FirstOrDefault(d =>
                    {
                        string dNorm = d.Name.Replace("_", "").Replace(" ", "").ToLowerInvariant();
                        return dNorm.Contains(normCar) || normCar.Contains(dNorm);
                    });

                if (carDir != null)
                {
                    DirectoryInfo? trackDir = null;
                    if (!string.IsNullOrWhiteSpace(track))
                    {
                        string normTrk = track.Replace("_", "").Replace(" ", "").ToLowerInvariant();
                        trackDir = carDir.GetDirectories()
                            .FirstOrDefault(d =>
                            {
                                string dNorm = d.Name.Replace("_", "").Replace(" ", "").ToLowerInvariant();
                                return dNorm.Contains(normTrk) || normTrk.Contains(dNorm);
                            });
                    }

                    targetSearchDir = trackDir ?? carDir;

                    if (!string.Equals(root, _watchDirectory, StringComparison.OrdinalIgnoreCase))
                    {
                        SetDirectory(root);
                    }
                    break;
                }
            }

            if (targetSearchDir == null || !targetSearchDir.Exists)
            {
                // Car/track folder not found. Do NOT fall back to other cars' setups!
                return null;
            }

            var jsonFiles = targetSearchDir.GetFiles("*.json", SearchOption.AllDirectories)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

            if (jsonFiles.Count == 0)
            {
                return null;
            }

            FileInfo? chosenFile = null;
            bool isLiveMatched = false;

            // Live telemetry matching: evaluate candidates against in-car physics & graphics state
            if (liveContext.HasValue && (liveContext.Value.Fuel > 0 || liveContext.Value.Tc > 0))
            {
                var ctx = liveContext.Value;
                int bestScore = -1;
                FileInfo? bestScoredFile = null;

                foreach (var file in jsonFiles)
                {
                    try
                    {
                        await using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                        int score = CalculateSetupMatchScore(doc.RootElement, ctx);

                        // Small preference for user-saved named files over generic Active.json if scores match
                        bool isGenericActive = file.Name.Equals("Active.json", StringComparison.OrdinalIgnoreCase);
                        if (!isGenericActive && score > 0)
                        {
                            score += 2;
                        }

                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestScoredFile = file;
                        }
                    }
                    catch { }
                }

                if (bestScore >= 50 && bestScoredFile != null)
                {
                    chosenFile = bestScoredFile;
                    isLiveMatched = true;
                }
            }

            // Fallback if no live match: use most recently written Active.json or newest file in this folder
            if (chosenFile == null)
            {
                var newest = jsonFiles[0];
                var active = jsonFiles.FirstOrDefault(f => f.Name.Equals("Active.json", StringComparison.OrdinalIgnoreCase));
                chosenFile = (active != null && (newest.FullName.Equals(active.FullName, StringComparison.OrdinalIgnoreCase) || Math.Abs((newest.LastWriteTimeUtc - active.LastWriteTimeUtc).TotalSeconds) <= 3))
                    ? active
                    : newest;
            }

            var payload = await ProcessSetupFileAsync(chosenFile.FullName, force: true, ct: ct);
            if (payload != null)
            {
                payload.IsLiveMatched = isLiveMatched;
                SetupDetected?.Invoke(payload);
            }
            return payload;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Scores a setup JSON against live telemetry (fuel level, TC, ABS, engine map, static camber).
    /// </summary>
    public static int CalculateSetupMatchScore(JsonElement root, in LiveCarSetupContext ctx)
    {
        int score = 0;
        if (!root.TryGetProperty("basicSetup", out var basic)) return 0;

        // 1. Strategy Fuel matching (crucial setup discriminator)
        if (ctx.Fuel > 0 && basic.TryGetProperty("strategy", out var strategy) && strategy.TryGetProperty("fuel", out var fuelProp))
        {
            float setupFuel = fuelProp.GetSingle();
            float diff = Math.Abs(setupFuel - ctx.Fuel);
            if (diff <= 0.6f) score += 40;
            else if (diff <= 1.5f) score += 25;
            else if (diff <= 3.0f) score += 10;
        }

        // 2. Electronics: TC1, ABS, EngineMap
        if (basic.TryGetProperty("electronics", out var electronics))
        {
            if (ctx.Tc > 0 && electronics.TryGetProperty("tC1", out var tcProp) && tcProp.GetInt32() == ctx.Tc)
            {
                score += 20;
            }
            if (ctx.Abs > 0 && electronics.TryGetProperty("abs", out var absProp) && absProp.GetInt32() == ctx.Abs)
            {
                score += 15;
            }
            if (ctx.EngineMap >= 0 && electronics.TryGetProperty("eCUMap", out var mapProp) && mapProp.GetInt32() == ctx.EngineMap)
            {
                score += 10;
            }
        }

        // 3. Static Camber matching (exact radians from Kunos physics engine)
        if (ctx.CamberRad != null && ctx.CamberRad.Length >= 4 && basic.TryGetProperty("alignment", out var alignment) && alignment.TryGetProperty("staticCamber", out var camberProp) && camberProp.ValueKind == JsonValueKind.Array)
        {
            var camberArr = camberProp.EnumerateArray().Select(x => x.GetSingle()).ToArray();
            if (camberArr.Length >= 4)
            {
                float maxDiff = 0f;
                for (int i = 0; i < 4; i++)
                {
                    maxDiff = Math.Max(maxDiff, Math.Abs(camberArr[i] - ctx.CamberRad[i]));
                }
                if (maxDiff <= 0.005f) score += 25;
                else if (maxDiff <= 0.02f) score += 15;
            }
        }

        return score;
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var payload = await ProcessSetupFileAsync(e.FullPath);
                if (payload != null)
                {
                    SetupDetected?.Invoke(payload);
                }
            }
            catch
            {
                // Suppress background thread unhandled exceptions
            }
        });
    }

    public async Task<SetupPayload?> ProcessSetupFileAsync(string filePath, bool force = false, CancellationToken ct = default)
    {
        if (!File.Exists(filePath)) return null;

        // ACC might briefly hold a write lock during flush. Retry up to 5 times with backoff.
        string? content = null;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                content = await reader.ReadToEndAsync(ct);
                break;
            }
            catch (IOException)
            {
                await Task.Delay(50 * (attempt + 1), ct);
            }
        }

        if (string.IsNullOrWhiteSpace(content)) return null;

        string currentHash = ComputeSha256(content);
        DateTime writeTime = File.GetLastWriteTimeUtc(filePath);

        lock (_lock)
        {
            bool sameHash = _fileHashes.TryGetValue(filePath, out string? lastHash) && lastHash == currentHash;
            bool sameTime = _fileTimestamps.TryGetValue(filePath, out DateTime lastTime) && lastTime == writeTime;

            if (!force && sameHash && sameTime)
            {
                // Skip duplicate I/O event
                return null;
            }

            _fileHashes[filePath] = currentHash;
            _fileTimestamps[filePath] = writeTime;
            LastSyncedFile = Path.GetFileName(filePath);
            LastSyncedHash = currentHash;
            LastSyncedTime = writeTime;
        }

        JsonElement jsonElement;
        try
        {
            using var doc = JsonDocument.Parse(content);
            jsonElement = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }

        // Parse carModel and track from directory path: Setups/<carModel>/<track>/<filename>.json
        string carModel = string.Empty;
        string track = string.Empty;

        var dirInfo = new FileInfo(filePath).Directory;
        if (dirInfo != null)
        {
            track = dirInfo.Name;
            if (dirInfo.Parent != null && !dirInfo.Parent.Name.Equals("Setups", StringComparison.OrdinalIgnoreCase))
            {
                carModel = dirInfo.Parent.Name;
            }
        }

        // If carName is inside the JSON itself, prefer that
        if (jsonElement.TryGetProperty("carName", out var carNameProp) && carNameProp.ValueKind == JsonValueKind.String)
        {
            carModel = carNameProp.GetString() ?? carModel;
        }

        return new SetupPayload
        {
            FileName = Path.GetFileName(filePath),
            CarModel = carModel,
            Track = track,
            Hash = currentHash,
            Timestamp = DateTime.UtcNow,
            SetupData = jsonElement
        };
    }

    public static string ComputeSha256(string content)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public void Dispose()
    {
        Stop();
    }
}
