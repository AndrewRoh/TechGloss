using TechGloss.Core.Contracts;
using TechGloss.GlossaryApi.Services;

namespace TechGloss.GlossaryApi.Endpoints;

public static class ExtractTermsEndpoint
{
    public static void MapExtractTerms(this WebApplication app)
    {
        // POST /glossary/extract-terms
        // TranslationOrchestrator가 번역 완료 후 비동기 호출
        // 추출 실패는 500 대신 빈 배열 + 200 반환 — 클라이언트가 예외 처리 불필요
        app.MapPost("/glossary/extract-terms", async (
            ExtractTermsRequest req,
            TermExtractionService extractor,
            CancellationToken ct) =>
        {
            var rows = await extractor.ExtractAsync(req, ct);
            return Results.Ok(rows);
        });
    }
}
