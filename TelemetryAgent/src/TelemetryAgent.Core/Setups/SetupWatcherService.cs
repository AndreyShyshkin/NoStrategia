using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TelemetryAgent.Core.Models;

namespace TelemetryAgent.Core.Setups;

/// <summary>
/// Monitors ACC vehicle setup JSON files, computes cryptographic SHA-256 hashes,
/// deduplicates rapid I/O write bursts, and dispatches change events to the ingestion pipeline.
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
        _watchDirectory = !string.IsNullOrWhiteSpace(customDirectory)
            ? customDirectory
            : GetDefaultAccSetupsPath();
    }

    public static string GetDefaultAccSetupsPath()
    {
        string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(docs, "Assetto Corsa Competizione", "Setups");
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

    public async Task<SetupPayload?> ProcessSetupFileAsync(string filePath, CancellationToken ct = default)
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
            if (_fileHashes.TryGetValue(filePath, out string? lastHash) && lastHash == currentHash)
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

        // Parse carModel and track from directory path if available: Setups/<carModel>/<track>/<filename>.json
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
