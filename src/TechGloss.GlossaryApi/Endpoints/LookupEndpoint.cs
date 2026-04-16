using Microsoft.EntityFrameworkCore;
using TechGloss.Core.Contracts;
using TechGloss.GlossaryApi.Data;

namespace TechGloss.GlossaryApi.Endpoints;

public static class LookupEndpoint
{
    public static void MapLookup(this WebApplication app)
    {
        app.MapGet("/glossary/lookup", async (
            string? q, string? lang, int? limit, Guid? category_id,
            GlossaryDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrEmpty(q)) return Results.Ok(Array.Empty<GlossaryLookupRow>());

            // 제어 문자 제거 (보안)
            q = new string(q.Where(c => !char.IsControl(c)).ToArray());
            if (string.IsNullOrEmpty(q)) return Results.Ok(Array.Empty<GlossaryLookupRow>());

            if (q.Length > 128) return Results.BadRequest("q exceeds maximum length of 128");

            var take = Math.Clamp(limit ?? 20, 1, 100);
            var langMode = lang ?? "auto";

            var escaped = q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            var pattern = $"%{escaped}%";

            var query = db.Entries.AsNoTracking();

            if (category_id.HasValue)
                query = query.Where(e => e.CategoryId == category_id);

            query = langMode switch
            {
                "ko" => query.Where(e => EF.Functions.Like(e.TermKo, pattern, "\\")),
                "en" => query.Where(e => EF.Functions.Like(e.TermEn, pattern, "\\")),
                _    => query.Where(e =>
                    EF.Functions.Like(e.TermKo, pattern, "\\") ||
                    EF.Functions.Like(e.TermEn, pattern, "\\"))
            };

            var entries = await query
                .OrderBy(e => e.TermEn)
                .Take(take)
                .ToListAsync(ct);

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

            var rows = entries.Select(e => new GlossaryLookupRow
            {
                Id = e.Id,
                TermKo = e.TermKo,
                TermEn = e.TermEn,
                DefinitionKo = e.DefinitionKo,
                CategoryName = e.CategoryId.HasValue && categories.TryGetValue(e.CategoryId.Value, out var n) ? n : null
            }).ToList();

            return Results.Ok(rows);
        });
    }
}
