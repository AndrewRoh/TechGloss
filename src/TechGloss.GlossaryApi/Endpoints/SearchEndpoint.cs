using Microsoft.EntityFrameworkCore;
using TechGloss.Core.Contracts;
using TechGloss.Core.Models;
using TechGloss.GlossaryApi.Data;
using TechGloss.GlossaryApi.Services;

namespace TechGloss.GlossaryApi.Endpoints;

public static class SearchEndpoint
{
    public static void MapSearch(this WebApplication app)
    {
        // IServiceProvider로 optional 서비스 resolve — 미등록 시 null → SQL LIKE fallback
        app.MapPost("/glossary/search", async (
            GlossarySearchRequest req,
            GlossaryDbContext db,
            IServiceProvider sp,
            CancellationToken ct) =>
        {
            var embedder = sp.GetService<EmbeddingService>();
            var qdrant   = sp.GetService<QdrantService>();
            return await HandleSearchAsync(req, db, embedder, qdrant, ct);
        });
    }

    private static async Task<IResult> HandleSearchAsync(
        GlossarySearchRequest req,
        GlossaryDbContext db,
        EmbeddingService? embedder,
        QdrantService? qdrant,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.QueryText))
            return Results.Ok(Array.Empty<GlossarySearchRow>());

        List<GlossaryEntry> entries;

        if (embedder is not null && qdrant is not null)
        {
            // Phase D: Qdrant 코사인 유사도 검색
            var vector = await embedder.EmbedAsync(req.QueryText, ct);
            var hitIds = await qdrant.SearchAsync(vector, req.TopK, req.CategoryName, ct);

            // Qdrant 유사도 순서(내림차순) 유지
            var orderMap = hitIds.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
            entries = (await db.Entries.AsNoTracking()
                .Where(e => hitIds.Contains(e.Id))
                .ToListAsync(ct))
                .OrderBy(e => orderMap.TryGetValue(e.Id, out var i) ? i : int.MaxValue)
                .ToList();
        }
        else
        {
            // MVP fallback: SQL LIKE
            var pattern = $"%{req.QueryText.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";

            Guid? filterCategoryId = null;
            if (!string.IsNullOrWhiteSpace(req.CategoryName))
            {
                var cat = await db.Categories.AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Name == req.CategoryName, ct);
                if (cat is not null) filterCategoryId = cat.Id;
            }

            var query = db.Entries.AsNoTracking()
                .Where(e => e.Status == "published")
                .Where(e =>
                    EF.Functions.Like(e.TermEn, pattern, "\\") ||
                    EF.Functions.Like(e.TermKo, pattern, "\\") ||
                    EF.Functions.Like(e.DefinitionKo, pattern, "\\"));

            if (filterCategoryId.HasValue)
                query = query.Where(e => e.CategoryId == filterCategoryId);

            entries = await query.OrderBy(e => e.TermEn).Take(req.TopK).ToListAsync(ct);
        }

        // CategoryId → CategoryName 조회 (공통)
        var categoryIds = entries
            .Where(e => e.CategoryId.HasValue)
            .Select(e => e.CategoryId!.Value)
            .Distinct()
            .ToList();

        var categories = categoryIds.Count > 0
            ? await db.Categories.AsNoTracking()
                .Where(c => categoryIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, ct)
            : new Dictionary<Guid, string>();

        var result = entries.Select(e => new GlossarySearchRow
        {
            EntryId      = e.Id,
            TermEn       = e.TermEn,
            TermKo       = e.TermKo,
            DefinitionKo = e.DefinitionKo,
            CategoryName = e.CategoryId.HasValue && categories.TryGetValue(e.CategoryId.Value, out var n) ? n : "",
            Source       = req.SourceLang == "en" ? e.TermEn : e.TermKo,
            Target       = req.TargetLang == "ko" ? e.TermKo : e.TermEn
        });

        return Results.Ok(result);
    }
}
