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

            // published 상태인 항목이 수정된 경우 Qdrant 벡터도 갱신
            if (entry.Status == "published")
            {
                var embedder = sp.GetService<EmbeddingService>();
                var qdrant   = sp.GetService<QdrantService>();
                if (embedder is not null && qdrant is not null)
                    await IndexToQdrantAsync(entry, db, embedder, qdrant, logger, ct);
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

            // Phase D: published 전환 시 Qdrant 벡터 인덱스 등록
            var embedder = sp.GetService<EmbeddingService>();
            var qdrant   = sp.GetService<QdrantService>();
            if (embedder is not null && qdrant is not null)
                await IndexToQdrantAsync(entry, db, embedder, qdrant, logger, ct);

            return Results.Ok();
        });
    }

    // upsert/publish 공통 Qdrant 인덱싱 — 실패 시 경고만, DB 저장 결과에 영향 없음
    private static async Task IndexToQdrantAsync(
        GlossaryEntry entry,
        GlossaryDbContext db,
        EmbeddingService embedder,
        QdrantService qdrant,
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
            var vector = await embedder.EmbedAsync(embedText, ct);

            await qdrant.UpsertAsync(entry.Id, vector, entry.TermEn, entry.TermKo, categoryName, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Qdrant 인덱싱 실패: EntryId={EntryId}", entry.Id);
        }
    }
}

public record PublishRequest(Guid EntryId);
