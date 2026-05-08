using System.Net.Http.Json;
using System.Text.Json;

namespace TechGloss.GlossaryApi.Services;

// Qdrant REST API 래퍼 — Qdrant.Client NuGet은 WPF/Infrastructure에서 금지이므로
// GlossaryApi 내부에서만 사용. HttpClient 직접 호출로 의존성 최소화.
public sealed class QdrantService
{
    private readonly HttpClient _http;
    private readonly string _endpoint;
    private readonly string _collection;
    private readonly int _vectorSize;
    private readonly ILogger<QdrantService> _log;

    public QdrantService(HttpClient http, IConfiguration config, ILogger<QdrantService> log)
    {
        _http       = http;
        _endpoint   = config["Qdrant:Endpoint"]       ?? "http://localhost:6333";
        _collection = config["Qdrant:CollectionName"] ?? "glossary";
        _vectorSize = config.GetValue<int>("Qdrant:VectorSize", 768);
        _log        = log;
    }

    // 컬렉션 생성 (없을 때만) — Program.cs 기동 시 호출
    public async Task EnsureCollectionAsync(CancellationToken ct = default)
    {
        var checkResp = await _http.GetAsync(
            $"{_endpoint}/collections/{_collection}", ct);
        if (checkResp.IsSuccessStatusCode) return;  // 이미 존재

        var createResp = await _http.PutAsJsonAsync(
            $"{_endpoint}/collections/{_collection}",
            new
            {
                vectors = new
                {
                    size     = _vectorSize,
                    distance = "Cosine"
                }
            }, ct);
        createResp.EnsureSuccessStatusCode();
        _log.LogInformation("Qdrant 컬렉션 '{Collection}' 생성 완료", _collection);
    }

    // 단건 벡터 upsert — publish 시 호출
    // pointId: GlossaryEntry.Id (Guid) — SQL ↔ 벡터 동기화 단순화
    public async Task UpsertPointAsync(
        Guid pointId, float[] vector,
        string termEn, string termKo, string categoryName,
        CancellationToken ct = default)
    {
        var resp = await _http.PutAsJsonAsync(
            $"{_endpoint}/collections/{_collection}/points",
            new
            {
                points = new[]
                {
                    new
                    {
                        id      = pointId.ToString(),
                        vector  = vector,
                        payload = new { term_en = termEn, term_ko = termKo, category = categoryName }
                    }
                }
            }, ct);
        resp.EnsureSuccessStatusCode();
        _log.LogDebug("Qdrant upsert: {PointId}", pointId);
    }

    // 코사인 유사도 TopK 검색 — SearchEndpoint Phase D에서 호출
    public async Task<List<Guid>> SearchAsync(
        float[] queryVector, int topK, string? categoryFilter = null,
        CancellationToken ct = default)
    {
        // 카테고리 필터: Qdrant payload filter 사용
        object? filter = categoryFilter is not null
            ? new { must = new[] { new { key = "category", match = new { value = categoryFilter } } } }
            : null;

        var body = new Dictionary<string, object?>
        {
            ["vector"]       = queryVector,
            ["limit"]        = topK,
            ["with_payload"] = false,
        };
        if (filter is not null) body["filter"] = filter;

        var resp = await _http.PostAsJsonAsync(
            $"{_endpoint}/collections/{_collection}/points/search", body, ct);
        resp.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(
            await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

        // 응답: { "result": [ { "id": "uuid", "score": 0.9 }, ... ] }
        return doc.RootElement.GetProperty("result")
            .EnumerateArray()
            .Select(e => Guid.Parse(e.GetProperty("id").GetString()!))
            .ToList();
    }

    public async Task DeleteAsync(Guid pointId, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync(
            $"{_endpoint}/collections/{_collection}/points/delete",
            new { points = new[] { pointId.ToString() } }, ct);
        resp.EnsureSuccessStatusCode();
    }
}
