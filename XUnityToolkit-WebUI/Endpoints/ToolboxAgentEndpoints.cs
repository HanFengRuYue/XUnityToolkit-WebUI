using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;

namespace XUnityToolkit_WebUI.Endpoints;

public static class ToolboxAgentEndpoints
{
    public static void MapToolboxAgentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/toolbox-agent");

        group.MapGet("/status", async (ToolboxAgentService service, CancellationToken ct) =>
            Results.Ok(ApiResult<ToolboxAgentStatus>.Ok(await service.GetStatusAsync(ct))));

        group.MapPost("/chat", async (
            ToolboxAgentChatRequest request,
            ToolboxAgentService service,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.SessionId))
                return Results.BadRequest(ApiResult.Fail("缺少智能体会话 ID。"));
            if (request.Message is null)
                return Results.BadRequest(ApiResult.Fail("消息不能为空。"));

            try
            {
                var response = await service.ChatAsync(request, ct);
                return Results.Ok(ApiResult<ToolboxAgentChatResponse>.Ok(response));
            }
            catch (ToolboxAgentUnavailableException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
            catch (InvalidDataException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(ApiResult.Fail(ex.Message));
            }
            catch (OperationCanceledException)
            {
                return Results.Json(ApiResult.Fail("智能体操作已取消。"), statusCode: 499);
            }
        });

        group.MapPost("/uploads", async (
            HttpRequest request,
            ToolboxAgentAttachmentStore store,
            CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest(ApiResult.Fail("请使用 multipart/form-data 上传附件。"));

            var form = await request.ReadFormAsync(ct);
            var sessionId = form["sessionId"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(sessionId))
                return Results.BadRequest(ApiResult.Fail("缺少智能体会话 ID。"));
            if (form.Files.Count == 0)
                return Results.BadRequest(ApiResult.Fail("没有选择附件。"));
            if (form.Files.Count > 8)
                return Results.BadRequest(ApiResult.Fail("一次最多上传 8 个附件。"));

            try
            {
                var uploaded = new List<ToolboxAgentAttachment>();
                foreach (var file in form.Files)
                    uploaded.Add(await store.SaveAsync(sessionId, file, ct));
                return Results.Ok(ApiResult<List<ToolboxAgentAttachment>>.Ok(uploaded));
            }
            catch (InvalidDataException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
        }).DisableAntiforgery();

        group.MapDelete("/sessions/{sessionId}", (
            string sessionId,
            ToolboxAgentService service) =>
        {
            try
            {
                service.ClearSession(sessionId);
                return Results.Ok(ApiResult.Ok());
            }
            catch (InvalidDataException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
        });
    }
}
