using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;
using Xunit;

namespace XUnityToolkit_WebUI.Tests.Services;

public sealed class ToolboxAgentConversationStoreTests
{
    [Fact]
    public async Task SaveAndLoad_TrimsVisibleMessagesAndReturnsNewestSummaryFirst()
    {
        var (root, store) = CreateStore();
        try
        {
            var older = CreateDocument(Guid.NewGuid().ToString(), "较早", DateTime.UtcNow.AddMinutes(-2));
            var newer = CreateDocument(Guid.NewGuid().ToString(), "较新", DateTime.UtcNow.AddMinutes(-1));
            newer.Messages = Enumerable.Range(0, ToolboxAgentConversationStore.MaxVisibleMessages + 1)
                .Select(index => Message(index.ToString()))
                .ToList();

            await store.SaveAsync(older);
            await store.SaveAsync(newer);

            var loaded = await store.LoadAsync(newer.SessionId);
            var summaries = await store.ListAsync();

            Assert.NotNull(loaded);
            Assert.Equal(ToolboxAgentConversationStore.MaxVisibleMessages, loaded!.Messages.Count);
            Assert.Equal("1", loaded.Messages[0].Text);
            Assert.Equal(newer.SessionId, summaries[0].SessionId);
            Assert.Equal(ToolboxAgentConversationStore.MaxVisibleMessages, summaries[0].MessageCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Save_EvictsOldestConversationAfterLimit()
    {
        var (root, store) = CreateStore();
        try
        {
            var firstId = Guid.NewGuid().ToString();
            for (var index = 0; index <= ToolboxAgentConversationStore.MaxConversations; index++)
            {
                var id = index == 0 ? firstId : Guid.NewGuid().ToString();
                await store.SaveAsync(CreateDocument(id, $"对话 {index}", DateTime.UtcNow.AddMinutes(index)));
            }

            var summaries = await store.ListAsync();

            Assert.Equal(ToolboxAgentConversationStore.MaxConversations, summaries.Count);
            Assert.DoesNotContain(summaries, item => item.SessionId == firstId);
            Assert.Null(await store.LoadAsync(firstId));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task List_IgnoresOneCorruptConversationFile()
    {
        var (root, store) = CreateStore();
        try
        {
            var valid = CreateDocument(Guid.NewGuid().ToString(), "有效", DateTime.UtcNow);
            await store.SaveAsync(valid);
            await File.WriteAllTextAsync(
                Path.Combine(root, "toolbox-agent", "conversations", $"{Guid.NewGuid()}.json"),
                "{not-json");

            var summaries = await store.ListAsync();

            var summary = Assert.Single(summaries);
            Assert.Equal(valid.SessionId, summary.SessionId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SelectCloudEndpoint_IsIndependentFromTranslationModeAndNeverFallsBackForManualChoice()
    {
        var settings = new AiTranslationSettings
        {
            Enabled = false,
            ActiveMode = "local",
            Endpoints =
            [
                new ApiEndpointConfig { Id = "local", Name = "本地", ApiKey = "local", Enabled = true, Priority = 100 },
                new ApiEndpointConfig { Id = "disabled", Name = "禁用", ApiKey = "key", Enabled = false, Priority = 20 },
                new ApiEndpointConfig { Id = "low", Name = "低优先级", ApiKey = "key-1", Enabled = true, Priority = 1 },
                new ApiEndpointConfig { Id = "high", Name = "高优先级", ApiKey = "key-2", Enabled = true, Priority = 8 }
            ]
        };

        var automatic = ToolboxAgentEndpointResolver.Resolve(settings);
        settings.AgentEndpointId = "low";
        var manual = ToolboxAgentEndpointResolver.Resolve(settings);
        settings.AgentEndpointId = "disabled";
        var missing = ToolboxAgentEndpointResolver.Resolve(settings);

        Assert.Equal("high", automatic.Endpoint?.Id);
        Assert.True(automatic.IsAutomatic);
        Assert.Equal("low", manual.Endpoint?.Id);
        Assert.False(manual.IsAutomatic);
        Assert.Null(missing.Endpoint);
        Assert.NotNull(missing.Error);
        Assert.DoesNotContain(
            ToolboxAgentEndpointResolver.GetAvailableCloudEndpoints(settings),
            endpoint => endpoint.Id is "local" or "disabled");
    }

    private static ToolboxAgentConversationDocument CreateDocument(
        string sessionId,
        string title,
        DateTime updatedAt) => new()
    {
        SessionId = sessionId,
        Title = title,
        CreatedAt = updatedAt.AddMinutes(-1),
        UpdatedAt = updatedAt,
        EndpointName = "云端",
        Messages = [Message(title)],
        ContextMessages = [new ToolboxAgentContextMessage("user", title)]
    };

    private static ToolboxAgentConversationMessage Message(string text) => new(
        Guid.NewGuid().ToString("N"),
        "user",
        text,
        [],
        [],
        DateTime.UtcNow);

    private static (string Root, ToolboxAgentConversationStore Store) CreateStore()
    {
        var root = Path.Combine(Path.GetTempPath(), $"xunity-agent-history-{Guid.NewGuid():N}");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AppData:Root"] = root })
            .Build();
        var paths = new AppDataPaths(configuration);
        paths.EnsureDirectoriesExist();
        return (root, new ToolboxAgentConversationStore(
            paths,
            NullLogger<ToolboxAgentConversationStore>.Instance));
    }
}
