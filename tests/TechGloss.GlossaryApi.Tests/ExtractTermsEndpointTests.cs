using System.Net;
using System.Net.Http.Json;
using TechGloss.Core.Contracts;

namespace TechGloss.GlossaryApi.Tests;

public class ExtractTermsEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ExtractTermsEndpointTests(CustomWebApplicationFactory factory)
        => _client = factory.CreateClient();

    [Fact]
    public async Task ExtractTerms_EmptyTexts_ReturnsEmptyArray()
    {
        // Ollama 미연결 환경에서도 빈 배열 + 200 반환 보장
        var req = new ExtractTermsRequest
        {
            SourceText     = "",
            TranslatedText = "",
            SourceLang     = "en",
            TargetLang     = "ko",
        };
        var resp = await _client.PostAsJsonAsync("/glossary/extract-terms", req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var rows = await resp.Content.ReadFromJsonAsync<List<ExtractedTermRow>>();
        Assert.NotNull(rows);
        Assert.Empty(rows!);
    }

    [Fact]
    public async Task ExtractTerms_ReturnsOk_WhenOllamaUnavailable()
    {
        // 핵심: Ollama 장애가 번역 파이프라인 전체를 중단시키지 않아야 함
        var req = new ExtractTermsRequest
        {
            SourceText     = "Deploy the container to Kubernetes cluster.",
            TranslatedText = "컨테이너를 쿠버네티스 클러스터에 배포합니다.",
            SourceLang     = "en",
            TargetLang     = "ko",
        };
        var resp = await _client.PostAsJsonAsync("/glossary/extract-terms", req);
        // 200이면 성공 — 실제 추출 여부는 Ollama 연결 상태에 따라 다름
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task ExtractTerms_InvalidRequest_StillReturnsOk()
    {
        // sourceText만 있고 translatedText 없는 경우 — 빈 배열 반환
        var req = new ExtractTermsRequest
        {
            SourceText     = "Some text",
            TranslatedText = "",
            SourceLang     = "en",
            TargetLang     = "ko",
        };
        var resp = await _client.PostAsJsonAsync("/glossary/extract-terms", req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
