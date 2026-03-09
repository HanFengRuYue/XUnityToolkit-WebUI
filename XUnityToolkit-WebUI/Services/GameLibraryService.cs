using System.Text.Json;
using System.Text.Json.Serialization;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class GameLibraryService(AppDataPaths paths, ILogger<GameLibraryService> logger)
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<List<Game>> GetAllAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return await ReadLibraryAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Game?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var games = await GetAllAsync(ct);
        return games.FirstOrDefault(g => g.Id == id);
    }

    public async Task<Game> AddAsync(string name, string gamePath, string? executableName = null, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var games = await ReadLibraryAsync(ct);

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
            logger.LogInformation("已添加游戏 {Name}，路径: {Path}", name, gamePath);
            return game;
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
            var games = await ReadLibraryAsync(ct);
            var index = games.FindIndex(g => g.Id == game.Id);
            if (index < 0) throw new KeyNotFoundException($"Game {game.Id} not found.");

            game.UpdatedAt = DateTime.UtcNow;
            games[index] = game;
            await WriteLibraryAsync(games, ct);
            return game;
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
            var games = await ReadLibraryAsync(ct);
            var removed = games.RemoveAll(g => g.Id == id);
            if (removed > 0)
            {
                await WriteLibraryAsync(games, ct);
                logger.LogInformation("已移除游戏 {Id}", id);
            }
            return removed > 0;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<Game>> ReadLibraryAsync(CancellationToken ct)
    {
        if (!File.Exists(paths.LibraryFile))
            return [];

        var json = await File.ReadAllTextAsync(paths.LibraryFile, ct);
        return JsonSerializer.Deserialize<List<Game>>(json, JsonOptions) ?? [];
    }

    private async Task WriteLibraryAsync(List<Game> games, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(games, JsonOptions);
        var tmpPath = paths.LibraryFile + ".tmp";
        await File.WriteAllTextAsync(tmpPath, json, ct);
        File.Move(tmpPath, paths.LibraryFile, overwrite: true);
    }
}
