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

            var pattern = $"%{req.QueryText.Replace("\\", "\\\\").Replace("%","\\%").Replace("_","\\_")}%";

            var rows = await db.Entries.AsNoTracking()
                .Where(e => e.Status == "published")
                .Where(e =>
                    EF.Functions.Like(e.TermEn, pattern, "\\") ||
                    EF.Functions.Like(e.TermKo, pattern, "\\") ||
                    EF.Functions.Like(e.DefinitionKo, pattern, "\\"))
                .OrderBy(e => e.TermEn)
                .Take(req.TopK)
                .ToListAsync(ct);

            var result = rows.Select(e => new GlossarySearchRow
            {
                EntryId      = e.Id,
                TermEn       = e.TermEn,
                TermKo       = e.TermKo,
                DefinitionKo = e.DefinitionKo,
                CategorySlug = e.CategoryId?.ToString() ?? "",
                Source       = req.SourceLang == "en" ? e.TermEn : e.TermKo,
                Target       = req.TargetLang == "ko" ? e.TermKo : e.TermEn
            });

            return Results.Ok(result);
        });
    }
}
