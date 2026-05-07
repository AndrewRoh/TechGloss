using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace TechGloss.GlossaryApi.Services;

public sealed class QdrantService
{
    private const string Collection = "glossary";
    private const ulong Dims = 768; // nomic-embed-text 출력 차원

    private readonly QdrantClient _qdrant;
    private readonly ILogger<QdrantService> _log;

    public QdrantService(QdrantClient qdrant, ILogger<QdrantService> log)
    {
        _qdrant = qdrant;
        _log    = log;
    }

    public async Task EnsureCollectionAsync(CancellationToken ct = default)
    {
        try
        {
            await _qdrant.GetCollectionInfoAsync(Collection, ct);
        }
        catch
        {
            await _qdrant.CreateCollectionAsync(
                Collection,
                new VectorParams { Size = Dims, Distance = Distance.Cosine },
                cancellationToken: ct);
            _log.LogInformation("Qdrant 컬렉션 '{Collection}' 생성 완료", Collection);
        }
    }

    // GlossaryEntry.Id == Qdrant point UUID — SQL ↔ 벡터 동기화 단순화
    public async Task UpsertAsync(
        Guid entryId, float[] vector,
        string termEn, string termKo, string categoryName,
        CancellationToken ct = default)
    {
        var vec = new Vector();
        vec.Data.AddRange(vector);

        var point = new PointStruct
        {
            Id      = new PointId { Uuid = entryId.ToString() },
            Vectors = new Vectors { Vector = vec }
        };
        point.Payload["term_en"]       = new Value { StringValue = termEn };
        point.Payload["term_ko"]       = new Value { StringValue = termKo };
        point.Payload["category_name"] = new Value { StringValue = categoryName };

        await _qdrant.UpsertAsync(Collection, new[] { point }, cancellationToken: ct);
        _log.LogDebug("Qdrant upsert: {EntryId}", entryId);
    }

    // 유사도 높은 순서로 EntryId 목록 반환 — 호출자가 SQL로 재조회해 DTO 변환
    public async Task<IReadOnlyList<Guid>> SearchAsync(
        float[] queryVector, uint topK, string? categoryName = null,
        CancellationToken ct = default)
    {
        Filter? filter = null;
        if (!string.IsNullOrWhiteSpace(categoryName))
        {
            filter = new Filter
            {
                Must =
                {
                    new Condition
                    {
                        Field = new FieldCondition
                        {
                            Key   = "category_name",
                            Match = new Match { Keyword = categoryName }
                        }
                    }
                }
            };
        }

        var hits = await _qdrant.SearchAsync(
            Collection, queryVector,
            limit: topK, filter: filter,
            cancellationToken: ct);

        return hits.Select(h => Guid.Parse(h.Id.Uuid)).ToList();
    }

    public async Task DeleteAsync(Guid entryId, CancellationToken ct = default)
    {
        // Qdrant.Client DeleteAsync(string, IReadOnlyList<Guid>, ...) 오버로드 사용
        await _qdrant.DeleteAsync(Collection, new[] { entryId }, cancellationToken: ct);
    }
}
