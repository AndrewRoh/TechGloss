using TechGloss.Core.Models;

namespace TechGloss.Core.Contracts;

public interface IGlossaryClient
{
    Task<IReadOnlyList<GlossarySearchRow>> SearchAsync(
        GlossarySearchRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<GlossaryLookupRow>> LookupAsync(
        string q, string lang = "auto", int limit = 20,
        Guid? categoryId = null, CancellationToken ct = default);

    Task UpsertAsync(GlossaryEntry entry, CancellationToken ct = default);
    Task PublishAsync(Guid entryId, CancellationToken ct = default);

    /// <summary>
    /// 번역 원문·결과를 GlossaryApi로 전송해 LLM이 용어 쌍을 추출하고 DB에 저장하게 한다.
    /// </summary>
    Task<IReadOnlyList<ExtractedTermRow>> ExtractTermsAsync(
        ExtractTermsRequest request, CancellationToken ct = default);
}
