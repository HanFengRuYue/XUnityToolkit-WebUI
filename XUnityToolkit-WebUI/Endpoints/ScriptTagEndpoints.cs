using System.Text.RegularExpressions;
using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;

namespace XUnityToolkit_WebUI.Endpoints;

public static class ScriptTagEndpoints
{
    public static void MapScriptTagEndpoints(this WebApplication app)
    {
        // Get global presets
        app.MapGet("/api/script-tag-presets", (ScriptTagService service) =>
        {
            var preset = service.LoadPreset();
            return Results.Ok(ApiResult<ScriptTagPreset>.Ok(preset));
        });

        // Get per-game script tag config
        app.MapGet("/api/games/{id}/script-tags", async (
            string id,
            GameLibraryService library,
            ScriptTagService service,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            var config = await service.GetAsync(id, ct);
            return Results.Ok(ApiResult<ScriptTagConfig>.Ok(config));
        });

        // Save per-game script tag config
        app.MapPut("/api/games/{id}/script-tags", async (
            string id,
            ScriptTagConfig config,
            GameLibraryService library,
            ScriptTagService service,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            if (config.Rules.Count > 100)
                return Results.Ok(ApiResult.Fail("规则数量不能超过 100 条"));

            foreach (var rule in config.Rules)
            {
                if (string.IsNullOrWhiteSpace(rule.Pattern))
                    return Results.Ok(ApiResult.Fail("正则表达式不能为空"));

                try
                {
                    var regex = new Regex(rule.Pattern, RegexOptions.None, TimeSpan.FromSeconds(1));

                    if (rule.Action == ScriptTagAction.Extract
                        && regex.GetGroupNumbers().Length < 2)
                    {
                        return Results.Ok(ApiResult.Fail(
                            $"Extract 规则必须包含至少一个捕获组 (...): {rule.Pattern}"));
                    }
                }
                catch (RegexParseException)
                {
                    return Results.Ok(ApiResult.Fail($"无效的正则表达式: {rule.Pattern}"));
                }
            }

            await service.SaveAsync(id, config, ct);
            return Results.Ok(ApiResult<ScriptTagConfig>.Ok(config));
        });
    }
}
