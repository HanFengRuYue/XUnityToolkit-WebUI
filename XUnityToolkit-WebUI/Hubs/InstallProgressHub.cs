using Microsoft.AspNetCore.SignalR;

namespace XUnityToolkit_WebUI.Hubs;

public sealed class InstallProgressHub : Hub
{
    public Task JoinGameGroup(string gameId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, $"game-{gameId}");

    public Task LeaveGameGroup(string gameId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, $"game-{gameId}");
}
