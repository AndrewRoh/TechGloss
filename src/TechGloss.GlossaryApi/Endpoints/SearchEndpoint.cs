using Microsoft.EntityFrameworkCore;
using TechGloss.Core.Contracts;
using TechGloss.GlossaryApi.Data;

namespace TechGloss.GlossaryApi.Endpoints;

public static class SearchEndpoint
{
    public static void MapSearch(this WebApplication app)
    {
        app.MapPost("/glossary/search", async (
            GlossarySearchRequest req, GlossaryDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.QueryText))
                return Results.Ok(Array.Empty<GlossarySearchRow>());

            var pattern = $"%{req.QueryText.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";

            // CategoryName 필터 적용 (선택)
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

            var entries = await query
                .OrderBy(e => e.TermEn)
                .Take(req.TopK)
                .ToListAsync(ct);

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
        });
    }
}
