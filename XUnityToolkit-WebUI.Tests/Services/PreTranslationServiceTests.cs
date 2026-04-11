using System.Collections.Concurrent;
using XUnityToolkit_WebUI.Services;
using Xunit;

namespace XUnityToolkit_WebUI.Tests.Services;

public sealed class PreTranslationServiceTests
{
    [Fact]
    public async Task SnapshotTranslations_ShouldNotThrow_WhenDictionaryMutatesConcurrently()
    {
        var source = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < 256; i++)
            source[$"key-{i:D4}"] = $"value-{i:D4}";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var writer = Task.Run(async () =>
        {
            var i = 256;
            while (!cts.IsCancellationRequested)
            {
                source[$"key-{i:D4}"] = $"value-{i:D4}";
                if (i >= 128)
                    source.TryRemove($"key-{i - 128:D4}", out _);

                i++;
                await Task.Yield();
            }
        });

        try
        {
            for (var i = 0; i < 200; i++)
            {
                var snapshot = PreTranslationService.SnapshotTranslations(source);
                var orderedKeys = snapshot.Keys.OrderBy(static key => key, StringComparer.Ordinal).ToArray();
                Assert.Equal(orderedKeys, snapshot.Keys.ToArray());
            }
        }
        finally
        {
            cts.Cancel();
            await writer;
        }
    }

    [Fact]
    public void FilterPersistableTranslations_ShouldKeepCurlyTemplatePlaceholderEntries()
    {
        var filtered = PreTranslationService.FilterPersistableTranslations(
        [
            KeyValuePair.Create("Hello {PLAYER}", "Hi {PLAYER}")
        ]);

        var entry = Assert.Single(filtered);
        Assert.Equal("Hello {PLAYER}", entry.Key);
        Assert.Equal("Hi {PLAYER}", entry.Value);
    }

    [Fact]
    public void FilterPersistableTranslations_ShouldRejectBrokenCurlyTemplatePlaceholderEntries()
    {
        var filtered = PreTranslationService.FilterPersistableTranslations(
        [
            KeyValuePair.Create("Hello {PLAYER}", "Hi {USER}")
        ]);

        Assert.Empty(filtered);
    }
}
