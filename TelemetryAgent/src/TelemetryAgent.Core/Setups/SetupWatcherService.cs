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
    private string _watchDirectory;
    private readonly Dictionary<string, string> _fileHashes = new(StringComparer.OrdinalIgnoreCase);
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
    /// Prioritizes directories containing existing setup files (e.g. bentley_continental_gt3_2018/Spa/Active.json).
    /// </summary>
    public static string FindAccSetupsDirectory()
    {
        var candidates = new List<string>();

        // 1. Standard Windows Documents
        try
        {
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrWhiteSpace(docs))
                candidates.Add(Path.Combine(docs, "Assetto Corsa Competizione", "Setups"));
        }
        catch { }

        // 2. User Profile Documents
        try
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userProfile))
                candidates.Add(Path.Combine(userProfile, "Documents", "Assetto Corsa Competizione", "Setups"));
        }
        catch { }

        // 3. Proton / Steam default user paths (e.g. C:\users\steamuser\Documents)
        string[] commonUsers = ["steamuser", "crossover", "wineuser", "default", "Public"];
        string[] rootDrives = ["C:\\", "D:\\", "Z:\\"];

        foreach (var drive in rootDrives)
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

        // Rank candidates: pick the first one that exists and contains .json setup files
        string? bestCandidateWithFiles = null;
        string? firstExisting = null;
        int maxFilesFound = 0;

        foreach (var path in candidates)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;

            try
            {
                if (Directory.Exists(path))
                {
                    firstExisting ??= path;
                    int jsonCount = Directory.GetFiles(path, "*.json", SearchOption.AllDirectories).Length;
                    if (jsonCount > maxFilesFound)
                    {
                        maxFilesFound = jsonCount;
                        bestCandidateWithFiles = path;
                    }
                }
            }
            catch { }
        }

        if (bestCandidateWithFiles != null)
            return bestCandidateWithFiles;

        if (firstExisting != null)
            return firstExisting;

        // Fallback default
        string defaultDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(defaultDocs, "Assetto Corsa Competizione", "Setups");
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_isActive) return;

            try
            {
                // Re-evaluate watch directory if current does not exist
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

                // Initial scan: immediately discover existing setups (e.g. Active.json)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var payload = await ScanExistingSetupsAsync();
                        if (payload != null)
                        {
                            SetupDetected?.Invoke(payload);
                        }
                    }
                    catch { }
                });
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

    public async Task<SetupPayload?> ScanExistingSetupsAsync(CancellationToken ct = default)
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

            // Prefer Active.json if recently saved, or newest setup
            var targetFile = files.FirstOrDefault(f => f.Name.Equals("Active.json", StringComparison.OrdinalIgnoreCase))
                             ?? files[0];

            return await ProcessSetupFileAsync(targetFile.FullName, force: true, ct);
        }
        catch
        {
            return null;
        }
    }

    public async Task<SetupPayload?> TryLoadSetupForCarAndTrackAsync(string carModel, string track, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(carModel) || !Directory.Exists(_watchDirectory))
            return null;

        try
        {
            var dirInfo = new DirectoryInfo(_watchDirectory);
            string normCar = carModel.Replace("_", "").Replace(" ", "").ToLowerInvariant();

            // Find car folder (e.g. bentley_continental_gt3_2018)
            var carDir = dirInfo.GetDirectories()
                .FirstOrDefault(d =>
                {
                    string dNorm = d.Name.Replace("_", "").Replace(" ", "").ToLowerInvariant();
                    return dNorm.Contains(normCar) || normCar.Contains(dNorm);
                });

            if (carDir == null) return null;

            // Find track folder (e.g. Spa)
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

            var searchDir = trackDir ?? carDir;
            var jsonFiles = searchDir.GetFiles("*.json", SearchOption.AllDirectories)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

            if (jsonFiles.Count == 0) return null;

            var target = jsonFiles.FirstOrDefault(f => f.Name.Equals("Active.json", StringComparison.OrdinalIgnoreCase))
                         ?? jsonFiles[0];

            var payload = await ProcessSetupFileAsync(target.FullName, force: true, ct);
            if (payload != null)
            {
                SetupDetected?.Invoke(payload);
            }
            return payload;
        }
        catch
        {
            return null;
        }
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

        lock (_lock)
        {
            if (!force && _fileHashes.TryGetValue(filePath, out string? lastHash) && lastHash == currentHash)
            {
                // Skip duplicate I/O event
                return null;
            }

            _fileHashes[filePath] = currentHash;
            LastSyncedFile = Path.GetFileName(filePath);
            LastSyncedHash = currentHash;
            LastSyncedTime = DateTime.UtcNow;
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
