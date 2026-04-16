using Microsoft.EntityFrameworkCore;
using TechGloss.Core.Contracts;
using TechGloss.Core.Models;
using TechGloss.GlossaryApi.Data;
using TechGloss.GlossaryApi.Services;

namespace TechGloss.GlossaryApi.Endpoints;

public static class ExtractTermsEndpoint
{
    public static void MapExtractTerms(this WebApplication app)
    {
        // POST /glossary/extract-terms
        // 번역 원문 + 번역 결과를 받아 LLM으로 용어 쌍을 추출하고 DB에 upsert 후 결과 반환
        app.MapPost("/glossary/extract-terms", async (
            ExtractTermsRequest request,
            TermExtractionService extractor,
            GlossaryDbContext db,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.SourceText) ||
                string.IsNullOrWhiteSpace(request.TranslatedText))
            {
                return Results.BadRequest("sourceText 및 translatedText는 필수입니다.");
            }

            // 1. Ollama로 용어 쌍 추출
            var rawTerms = await extractor.ExtractAsync(
                request.SourceText, request.TranslatedText,
                request.SourceLang, request.TargetLang, ct);

            if (rawTerms.Count == 0)
                return Results.Ok(Array.Empty<ExtractedTermRow>());

            // 2. 각 용어 쌍 upsert
            var results = new List<ExtractedTermRow>(rawTerms.Count);
            foreach (var raw in rawTerms)
            {
                var enNorm = raw.TermEn.Trim().ToLowerInvariant();
                var koNorm = raw.TermKo.Trim()
                    .Normalize(System.Text.NormalizationForm.FormKC)
                    .ToLowerInvariant();

                // 카테고리 조회 (없으면 null)
                var category = await db.Categories
                    .FirstOrDefaultAsync(c => c.Name == raw.CategoryName, ct);

                // 동일 영문 정규형 중복 확인
                var found = await db.Entries
                    .FirstOrDefaultAsync(e => e.TermEnNormalized == enNorm, ct);

                bool isNew = found is null;
                GlossaryEntry entry;
                if (found is null)
                {
                    entry = new GlossaryEntry
                    {
                        Id                = Guid.NewGuid(),
                        TermEn            = raw.TermEn,
                        TermKo            = raw.TermKo,
                        TermEnNormalized  = enNorm,
                        TermKoNormalized  = koNorm,
                        CategoryId        = category?.Id,
                        Status            = "draft",
                        CreatedAt         = DateTimeOffset.UtcNow,
                        UpdatedAt         = DateTimeOffset.UtcNow,
                    };
                    db.Entries.Add(entry);
                }
                else
                {
                    entry = found;
                    if (string.IsNullOrWhiteSpace(found.TermKo))
                    {
                        // 한글 용어가 비어 있을 때만 보완
                        found.TermKo           = raw.TermKo;
                        found.TermKoNormalized = koNorm;
                        found.UpdatedAt        = DateTimeOffset.UtcNow;
                    }
                }

                results.Add(new ExtractedTermRow
                {
                    EntryId      = entry.Id,
                    TermEn       = entry.TermEn,
                    TermKo       = entry.TermKo,
                    CategoryName = category?.Name ?? raw.CategoryName,
                    IsNew        = isNew,
                });
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(results);
        });
    }
}
