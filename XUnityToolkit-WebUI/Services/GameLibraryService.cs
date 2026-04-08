using System.Text.Json;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class GameLibraryService(AppDataPaths paths, ILogger<GameLibraryService> logger)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private volatile bool _loaded;
    private List<Game> _gamesSnapshot = [];
    private Dictionary<string, Game> _gamesById = new(StringComparer.Ordinal);

    public async Task<List<Game>> GetAllAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        return CloneGames(_gamesSnapshot);
    }

    public async Task<Game?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        return _gamesById.TryGetValue(id, out var game) ? CloneGame(game) : null;
    }

    public async Task<Game> AddAsync(string name, string gamePath, string? executableName = null, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await EnsureLoadedCoreAsync(ct);
            var games = CloneGames(_gamesSnapshot);

            if (games.Any(g => g.GamePath.Equals(gamePath, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Game path already exists in library.");

            var game = new Game
            {
                Name = name,
                GamePath = gamePath,
                ExecutableName = executableName
            };

            games.Add(game);
            await WriteLibraryAsync(games, ct);
            ReplaceSnapshot(games);
            logger.LogInformation("Added game {Name} at {Path}", name, gamePath);
            return CloneGame(game);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Game> UpdateAsync(Game game, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await EnsureLoadedCoreAsync(ct);
            var games = CloneGames(_gamesSnapshot);
            var index = games.FindIndex(g => g.Id == game.Id);
            if (index < 0)
                throw new KeyNotFoundException($"Game {game.Id} not found.");

            game.UpdatedAt = DateTime.UtcNow;
            games[index] = CloneGame(game);
            await WriteLibraryAsync(games, ct);
            ReplaceSnapshot(games);
            return CloneGame(game);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> RemoveAsync(string id, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await EnsureLoadedCoreAsync(ct);
            var games = CloneGames(_gamesSnapshot);
            var removed = games.RemoveAll(g => g.Id == id);
            if (removed > 0)
            {
                await WriteLibraryAsync(games, ct);
                ReplaceSnapshot(games);
                logger.LogInformation("Removed game {Id}", id);
            }

            return removed > 0;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_loaded)
            return;

        await _lock.WaitAsync(ct);
        try
        {
            await EnsureLoadedCoreAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task EnsureLoadedCoreAsync(CancellationToken ct)
    {
        if (_loaded)
            return;

        var games = await ReadLibraryAsync(ct);
        ReplaceSnapshot(games);
    }

    private async Task<List<Game>> ReadLibraryAsync(CancellationToken ct)
    {
        if (!File.Exists(paths.LibraryFile))
            return [];

        var json = await File.ReadAllTextAsync(paths.LibraryFile, ct);
        return JsonSerializer.Deserialize<List<Game>>(json, FileHelper.DataJsonOptions) ?? [];
    }

    private async Task WriteLibraryAsync(List<Game> games, CancellationToken ct)
    {
        await FileHelper.WriteJsonAtomicAsync(paths.LibraryFile, games, options: null, ct);
    }

    private void ReplaceSnapshot(List<Game> games)
    {
        var snapshot = CloneGames(games);
        _gamesSnapshot = snapshot;
        _gamesById = snapshot.ToDictionary(g => g.Id, StringComparer.Ordinal);
        _loaded = true;
    }

    private static List<Game> CloneGames(IEnumerable<Game> games)
    {
        return games.Select(CloneGame).ToList();
    }

    private static Game CloneGame(Game game)
    {
        return new Game
        {
            Id = game.Id,
            Name = game.Name,
            GamePath = game.GamePath,
            ExecutableName = game.ExecutableName,
            AddedAt = game.AddedAt,
            UpdatedAt = game.UpdatedAt,
            IsUnityGame = game.IsUnityGame,
            DetectedInfo = game.DetectedInfo is null
                ? null
                : new UnityGameInfo
                {
                    UnityVersion = game.DetectedInfo.UnityVersion,
                    Backend = game.DetectedInfo.Backend,
                    Architecture = game.DetectedInfo.Architecture,
                    DetectedExecutable = game.DetectedInfo.DetectedExecutable,
                    DetectedAt = game.DetectedInfo.DetectedAt,
                    HasTextMeshPro = game.DetectedInfo.HasTextMeshPro
                },
            InstallState = game.InstallState,
            DetectedFrameworks = game.DetectedFrameworks?.Select(framework => new DetectedModFramework
            {
                Framework = framework.Framework,
                Version = framework.Version,
                HasXUnityPlugin = framework.HasXUnityPlugin,
                XUnityVersion = framework.XUnityVersion
            }).ToList(),
            InstalledBepInExVersion = game.InstalledBepInExVersion,
            InstalledXUnityVersion = game.InstalledXUnityVersion,
            SteamAppId = game.SteamAppId,
            SteamGridDbGameId = game.SteamGridDbGameId,
            LastPlayedAt = game.LastPlayedAt,
            AiDescription = game.AiDescription,
            HasCover = game.HasCover,
            HasBackground = game.HasBackground
        };
    }
}
