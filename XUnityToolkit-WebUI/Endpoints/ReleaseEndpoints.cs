using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;

namespace XUnityToolkit_WebUI.Endpoints;

public static class ReleaseEndpoints
{
    public static void MapReleaseEndpoints(this WebApplication app)
    {
        app.MapGet("/api/releases/bepinex", async (GitHubReleaseService gitHub) =>
        {
            try
            {
                var releases = await gitHub.GetReleasesAsync("BepInEx", "BepInEx");
                return Results.Ok(ApiResult<List<GitHubRelease>>.Ok(releases));
            }
            catch (HttpRequestException)
            {
                return Results.StatusCode(503);
            }
        });

        app.MapGet("/api/releases/xunity", async (GitHubReleaseService gitHub) =>
        {
            try
            {
                var releases = await gitHub.GetReleasesAsync("bbepis", "XUnity.AutoTranslator");
                return Results.Ok(ApiResult<List<GitHubRelease>>.Ok(releases));
            }
            catch (HttpRequestException)
            {
                return Results.StatusCode(503);
            }
        });
    }
}
