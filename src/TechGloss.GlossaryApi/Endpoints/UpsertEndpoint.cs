using Microsoft.EntityFrameworkCore;
using TechGloss.Core.Models;
using TechGloss.GlossaryApi.Data;
using TechGloss.GlossaryApi.Services;

namespace TechGloss.GlossaryApi.Endpoints;

public static class UpsertEndpoint
{
    public static void MapUpsertAndPublish(this WebApplication app)
    {
        app.MapPost("/glossary/upsert", async (
            GlossaryEntry entry,
            GlossaryDbContext db,
            IServiceProvider sp,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            entry.TermKoNormalized = entry.TermKo.Trim().Normalize(
                System.Text.NormalizationForm.FormKC).ToLowerInvariant();
            entry.TermEnNormalized = entry.TermEn.Trim().ToLowerInvariant();
            entry.UpdatedAt = DateTimeOffset.UtcNow;

            var exists = await db.Entries.AnyAsync(e => e.Id == entry.Id, ct);
            if (!exists)
            {
                entry.CreatedAt = entry.UpdatedAt;
                entry.Status = "draft";
                db.Entries.Add(entry);
            }
            else
            {
                db.Entries.Update(entry);
            }
            await db.SaveChangesAsync(ct);

            // published 상태면 embedding 갱신
            if (entry.Status == "published")
            {
                var embedder = sp.GetService<EmbeddingService>();
                if (embedder is not null)
                    await EmbedEntryAsync(entry, db, embedder, logger, ct);
            }

            return Results.Ok(new { entry.Id });
        });

        app.MapPost("/glossary/publish", async (
            PublishRequest req,
            GlossaryDbContext db,
            IServiceProvider sp,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            var entry = await db.Entries.FindAsync(new object[] { req.EntryId }, ct);
            if (entry is null) return Results.NotFound();

            entry.Status    = "published";
            entry.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            var embedder = sp.GetService<EmbeddingService>();
            if (embedder is not null)
                await EmbedEntryAsync(entry, db, embedder, logger, ct);

            return Results.Ok();
        });
    }

    private static async Task EmbedEntryAsync(
        GlossaryEntry entry,
        GlossaryDbContext db,
        EmbeddingService embedder,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            var categoryName = "General";
            if (entry.CategoryId.HasValue)
            {
                var cat = await db.Categories.AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == entry.CategoryId.Value, ct);
                if (cat is not null) categoryName = cat.Name;
            }

            var embedText = EmbeddingService.BuildEmbedText(
                categoryName, entry.TermEn, entry.TermKo, entry.DefinitionKo);
            entry.Embedding = await embedder.EmbedAsync(embedText, ct);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "임베딩 실패: EntryId={EntryId}", entry.Id);
        }
    }
}

public record PublishRequest(Guid EntryId);
