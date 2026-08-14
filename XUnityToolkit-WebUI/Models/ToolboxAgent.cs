using System.Text.Json;

namespace XUnityToolkit_WebUI.Models;

public enum AgentToolExecutionState
{
    Completed,
    Failed,
    Skipped,
    RequiresConfirmation
}

public enum PluginRepairActionState
{
    Completed,
    Failed,
    Skipped
}

public record ToolboxAgentStatus(
    bool Supported,
    string? Reason,
    string? EndpointName
);

public record ToolboxAgentAttachment(
    string Id,
    string FileName,
    string Kind,
    long FileSize
);

public record ToolboxAgentChatRequest(
    string SessionId,
    string Message,
    string? GameId = null,
    List<string>? AttachmentIds = null,
    bool ConfirmPendingAction = false
);

public record ToolboxAgentToolExecution(
    string Id,
    string Tool,
    string Description,
    AgentToolExecutionState State,
    string Message
);

public record ToolboxAgentChatResponse(
    string SessionId,
    string Message,
    List<ToolboxAgentToolExecution> Executions,
    bool RequiresConfirmation,
    string? PendingActionDescription,
    string EndpointName
);

public record PluginRepairActionResult(
    string Id,
    string Tool,
    string Description,
    PluginRepairActionState State,
    string Message,
    string? Target = null
);

public record PluginAutoRepairResult(
    PluginHealthReport Before,
    PluginHealthReport After,
    List<PluginRepairActionResult> Actions,
    string Summary,
    string EndpointName,
    DateTime RepairedAt
);

internal sealed class ToolboxAgentTurn
{
    public string? Message { get; set; }
    public List<ToolboxAgentToolCall>? ToolCalls { get; set; }
}

internal sealed class ToolboxAgentToolCall
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public JsonElement Arguments { get; set; }
}

internal sealed class PluginRepairPlanResponse
{
    public string? Summary { get; set; }
    public List<PluginRepairPlanAction>? Actions { get; set; }
}

internal sealed class PluginRepairPlanAction
{
    public string? Tool { get; set; }
    public string? Description { get; set; }
    public string? ArtifactId { get; set; }
    public string? RelativePath { get; set; }
    public string? Section { get; set; }
    public string? Key { get; set; }
    public string? Value { get; set; }
    public string? Component { get; set; }
}
