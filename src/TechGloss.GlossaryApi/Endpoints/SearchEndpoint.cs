using Microsoft.EntityFrameworkCore;
using Pgvector;
using TechGloss.Core.Contracts;
using TechGloss.Core.Models;
using TechGloss.GlossaryApi.Data;
using TechGloss.GlossaryApi.Services;

namespace TechGloss.GlossaryApi.Endpoints;

public static class SearchEndpoint
{
    public static void MapSearch(this WebApplication app)
    {
        app.MapPost("/glossary/search", async (
            GlossarySearchRequest req,
            GlossaryDbContext db,
            IServiceProvider sp,
            CancellationToken ct) =>
        {
            var embedder = sp.GetService<EmbeddingService>();
            return await HandleSearchAsync(req, db, embedder, ct);
        });
    }

    private static async Task<IResult> HandleSearchAsync(
        GlossarySearchRequest req,
        GlossaryDbContext db,
        EmbeddingService? embedder,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.QueryText))
            return Results.Ok(Array.Empty<GlossarySearchRow>());

        List<GlossaryEntry> entries;

        Guid? filterCategoryId = null;
        if (!string.IsNullOrWhiteSpace(req.CategoryName))
        {
            var cat = await db.Categories.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Name == req.CategoryName, ct);
            if (cat is not null) filterCategoryId = cat.Id;
        }

        if (embedder is not null && db.Database.IsNpgsql())
        {
            // pgvector 코사인 유사도 검색 (HNSW 인덱스)
            var queryVector = await embedder.EmbedAsync(req.QueryText, ct);
            var vectorParam = new Vector(queryVector);

            FormattableString sql;
            if (filterCategoryId.HasValue)
                sql = $"SELECT * FROM glossary_entry WHERE status = 'published' AND category_id = {filterCategoryId.Value} AND embedding IS NOT NULL ORDER BY embedding <=> {vectorParam} LIMIT {req.TopK}";
            else
                sql = $"SELECT * FROM glossary_entry WHERE status = 'published' AND embedding IS NOT NULL ORDER BY embedding <=> {vectorParam} LIMIT {req.TopK}";

            entries = await db.Entries.FromSqlInterpolated(sql).AsNoTracking().ToListAsync(ct);
        }
        else
        {
            // Fallback: SQL LIKE 검색
            var pattern = $"%{req.QueryText.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";

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

        // CategoryId → CategoryName 조회
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
