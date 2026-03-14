using Microsoft.AspNetCore.SignalR;

namespace XUnityToolkit_WebUI.Hubs;

public sealed class InstallProgressHub : Hub
{
    public Task JoinGameGroup(string gameId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, $"game-{gameId}");

    public Task LeaveGameGroup(string gameId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, $"game-{gameId}");

    public Task JoinAiTranslationGroup() =>
        Groups.AddToGroupAsync(Context.ConnectionId, "ai-translation");

    public Task LeaveAiTranslationGroup() =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, "ai-translation");

    public Task JoinLogGroup() =>
        Groups.AddToGroupAsync(Context.ConnectionId, "logs");

    public Task LeaveLogGroup() =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, "logs");

    public Task JoinPreTranslationGroup(string gameId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, $"pre-translation-{gameId}");

    public Task LeavePreTranslationGroup(string gameId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, $"pre-translation-{gameId}");

    public Task JoinLocalLlmGroup() =>
        Groups.AddToGroupAsync(Context.ConnectionId, "local-llm");

    public Task LeaveLocalLlmGroup() =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, "local-llm");

    public Task JoinFontReplacementGroup(string gameId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, $"font-replacement-{gameId}");

    public Task LeaveFontReplacementGroup(string gameId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, $"font-replacement-{gameId}");
}
