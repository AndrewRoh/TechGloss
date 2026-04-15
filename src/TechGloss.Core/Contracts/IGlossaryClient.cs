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
}
