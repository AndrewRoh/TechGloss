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

            // 2. 카테고리 캐시 구성 (기존 조회 + 신규 생성 추적)
            var categoryCache = (await db.Categories.ToListAsync(ct))
                .ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

            // 3. 각 용어 쌍 upsert
            var results = new List<ExtractedTermRow>(rawTerms.Count);
            foreach (var raw in rawTerms)
            {
                var enNorm = raw.TermEn.Trim().ToLowerInvariant();
                var koNorm = raw.TermKo.Trim()
                    .Normalize(System.Text.NormalizationForm.FormKC)
                    .ToLowerInvariant();

                // 카테고리 조회 — 없으면 신규 생성
                if (!categoryCache.TryGetValue(raw.CategoryName, out var category))
                {
                    category = new GlossaryCategory { Id = Guid.NewGuid(), Name = raw.CategoryName };
                    db.Categories.Add(category);
                    categoryCache[raw.CategoryName] = category;
                }

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
                        DefinitionKo      = raw.DefinitionKo,
                        CategoryId        = category.Id,
                        Status            = "draft",
                        CreatedAt         = DateTimeOffset.UtcNow,
                        UpdatedAt         = DateTimeOffset.UtcNow,
                    };
                    db.Entries.Add(entry);
                }
                else
                {
                    entry = found;
                    var updated = false;

                    if (string.IsNullOrWhiteSpace(found.TermKo))
                    {
                        found.TermKo           = raw.TermKo;
                        found.TermKoNormalized = koNorm;
                        updated = true;
                    }
                    if (string.IsNullOrWhiteSpace(found.DefinitionKo) &&
                        !string.IsNullOrWhiteSpace(raw.DefinitionKo))
                    {
                        found.DefinitionKo = raw.DefinitionKo;
                        updated = true;
                    }
                    if (found.CategoryId is null)
                    {
                        found.CategoryId = category.Id;
                        updated = true;
                    }
                    if (updated) found.UpdatedAt = DateTimeOffset.UtcNow;
                }

                results.Add(new ExtractedTermRow
                {
                    EntryId      = entry.Id,
                    TermEn       = entry.TermEn,
                    TermKo       = entry.TermKo,
                    CategoryName = category.Name,
                    IsNew        = isNew,
                });
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(results);
        });
    }
}
