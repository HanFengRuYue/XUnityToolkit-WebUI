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
    string? EndpointName,
    List<ToolboxAgentEndpointOption> Endpoints
);

public record ToolboxAgentEndpointOption(
    string Id,
    string Name,
    LlmProvider Provider,
    string ModelName,
    bool IsAutomaticDefault
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
    bool ConfirmPendingAction = false,
    string? EndpointId = null
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
    string EndpointId,
    string EndpointName
);

public record ToolboxAgentConversationMessage(
    string Id,
    string Role,
    string Text,
    List<ToolboxAgentAttachment> Attachments,
    List<ToolboxAgentToolExecution> Executions,
    DateTime CreatedAt
);

public record ToolboxAgentConversationSummary(
    string SessionId,
    string Title,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? EndpointId,
    string? EndpointName,
    string? GameId,
    int MessageCount
);

public record ToolboxAgentConversation(
    ToolboxAgentConversationSummary Summary,
    List<ToolboxAgentConversationMessage> Messages
);

internal sealed record ToolboxAgentContextMessage(string Role, string Content);

internal sealed class ToolboxAgentConversationDocument
{
    public int Version { get; set; } = 1;
    public string SessionId { get; set; } = "";
    public string Title { get; set; } = "新对话";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? EndpointId { get; set; }
    public string? EndpointName { get; set; }
    public string? GameId { get; set; }
    public List<ToolboxAgentConversationMessage> Messages { get; set; } = [];
    public List<ToolboxAgentContextMessage> ContextMessages { get; set; } = [];

    public ToolboxAgentConversationSummary ToSummary() => new(
        SessionId,
        Title,
        CreatedAt,
        UpdatedAt,
        EndpointId,
        EndpointName,
        GameId,
        Messages.Count);

    public ToolboxAgentConversation ToPublic() => new(ToSummary(), Messages);
}

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
