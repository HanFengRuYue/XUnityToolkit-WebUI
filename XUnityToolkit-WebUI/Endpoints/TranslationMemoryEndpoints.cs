using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;

namespace XUnityToolkit_WebUI.Endpoints;

public static class TranslationMemoryEndpoints
{
    public static void MapTranslationMemoryEndpoints(this WebApplication app)
    {
        // TM stats
        app.MapGet("/api/games/{id}/translation-memory/stats", (
            string id,
            TranslationMemoryService tmService) =>
        {
            if (!Guid.TryParse(id, out _))
                return Results.BadRequest(ApiResult.Fail("Invalid game ID"));
            var (exact, pattern, fuzzy, misses) = tmService.GetHitStats();
            return Results.Ok(ApiResult<object>.Ok(new
            {
                entryCount = tmService.GetEntryCount(id),
                exactHits = exact,
                fuzzyHits = fuzzy,
                patternHits = pattern,
                misses
            }));
        });

        // Clear TM
        app.MapDelete("/api/games/{id}/translation-memory", async (
            string id,
            TranslationMemoryService tmService) =>
        {
            if (!Guid.TryParse(id, out _))
                return Results.BadRequest(ApiResult.Fail("Invalid game ID"));
            await tmService.DeleteAsync(id);
            return Results.Ok(ApiResult.Ok());
        });

        // Dynamic patterns
        app.MapGet("/api/games/{id}/dynamic-patterns", async (
            string id,
            DynamicPatternService patternService,
            CancellationToken ct) =>
        {
            if (!Guid.TryParse(id, out _))
                return Results.BadRequest(ApiResult.Fail("Invalid game ID"));
            var store = await patternService.GetPatternsAsync(id, ct);
            return Results.Ok(ApiResult<DynamicPatternStore>.Ok(store));
        });

        app.MapDelete("/api/games/{id}/dynamic-patterns", async (
            string id,
            DynamicPatternService patternService) =>
        {
            if (!Guid.TryParse(id, out _))
                return Results.BadRequest(ApiResult.Fail("Invalid game ID"));
            await patternService.DeleteAsync(id);
            return Results.Ok(ApiResult.Ok());
        });

        // Term candidates
        app.MapGet("/api/games/{id}/term-candidates", async (
            string id,
            TermExtractionService extractionService,
            CancellationToken ct) =>
        {
            if (!Guid.TryParse(id, out _))
                return Results.BadRequest(ApiResult.Fail("Invalid game ID"));
            var store = await extractionService.GetCandidatesAsync(id, ct);
            return Results.Ok(ApiResult<TermCandidateStore>.Ok(store));
        });

        app.MapPost("/api/games/{id}/term-candidates/apply", async (
            string id,
            ApplyTermCandidatesRequest request,
            TermExtractionService extractionService,
            PreTranslationService preTranslationService,
            CancellationToken ct) =>
        {
            var count = await extractionService.ApplyCandidatesAsync(id, request.Originals, ct);
            // Resume paused pre-translation if waiting for term review
            preTranslationService.ResumeAfterTermReview(id);
            return Results.Ok(ApiResult<int>.Ok(count));
        });

        app.MapDelete("/api/games/{id}/term-candidates", async (
            string id,
            TermExtractionService extractionService) =>
        {
            if (!Guid.TryParse(id, out _))
                return Results.BadRequest(ApiResult.Fail("Invalid game ID"));
            await extractionService.DeleteCandidatesAsync(id);
            return Results.Ok(ApiResult.Ok());
        });
    }
}

public record ApplyTermCandidatesRequest(IList<string>? Originals);
