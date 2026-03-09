using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;

namespace XUnityToolkit_WebUI.Endpoints;

public static class InstallEndpoints
{
    public static void MapInstallEndpoints(this WebApplication app)
    {
        app.MapGet("/api/games/{id}/status", (string id, InstallOrchestrator orchestrator) =>
        {
            var status = orchestrator.GetStatus(id);
            return Results.Ok(ApiResult<InstallationStatus>.Ok(status));
        });

        app.MapPost("/api/games/{id}/install", async (string id,
            InstallRequest? request,
            InstallOrchestrator orchestrator) =>
        {
            try
            {
                var status = await orchestrator.StartInstallAsync(id, request?.Config);
                return Results.Accepted($"/api/games/{id}/status",
                    ApiResult<InstallationStatus>.Ok(status));
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(ApiResult<InstallationStatus>.Fail(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(ApiResult<InstallationStatus>.Fail(ex.Message));
            }
        });

        app.MapDelete("/api/games/{id}/install", async (string id,
            InstallOrchestrator orchestrator) =>
        {
            try
            {
                var status = await orchestrator.StartUninstallAsync(id);
                return Results.Accepted($"/api/games/{id}/status",
                    ApiResult<InstallationStatus>.Ok(status));
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(ApiResult<InstallationStatus>.Fail(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(ApiResult<InstallationStatus>.Fail(ex.Message));
            }
        });

        app.MapPost("/api/games/{id}/cancel", (string id, InstallOrchestrator orchestrator) =>
        {
            orchestrator.Cancel(id);
            return Results.Ok(ApiResult.Ok());
        });
    }
}

public record InstallRequest(XUnityConfig? Config = null);
