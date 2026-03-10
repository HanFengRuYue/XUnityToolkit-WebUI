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
}
