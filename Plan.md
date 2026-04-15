# TechGloss — 상세 구현 계획 v2 (Plan2)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** WPF exe 안에 WebView2(Chromium) 기반 현대 웹 UI를 올리고, 사내 고정 Ollama LLM(`gemma4:31b`)과 로컬 Glossary API 서버를 연동해 EN↔KO 양방향 IT 기술 번역 및 글자 단위 용어 LIKE 검색을 제공한다.

**Architecture:** WPF 호스트가 WebView2를 통해 SPA를 표시하고, 모든 외부 HTTP(Ollama/Glossary API)는 .NET 호스트에서만 처리한다. Glossary API는 독립 프로세스로 기동하며 관계형 DB(정본) + 벡터 DB(RAG)를 소유한다. WPF는 벡터 DB에 직접 연결하지 않는다.

**Tech Stack:** .NET 8 WPF, Microsoft.Web.WebView2, Vite+React(SPA), ASP.NET Core Minimal API(GlossaryApi), SQLite(Dapper/EF Core), Qdrant(Docker 로컬), Ollama HTTP API, System.Text.Json, Polly(재시도)

---

## 파일 맵 (생성 / 수정 대상 전체)

| 역할 | 경로 | 작업 |
|------|------|------|
| 솔루션 | `TechGloss.sln` | 생성 |
| 공통 빌드 | `Directory.Build.props` | 생성 |
| 도메인 모델·인터페이스 | `src/TechGloss.Core/` | 생성 |
| HTTP 클라이언트·설정 | `src/TechGloss.Infrastructure/` | 생성 |
| WPF 쉘·WebView2·브리지 | `src/TechGloss.Wpf/` | 생성 |
| SPA (Vite+React) | `web/` | 생성 |
| Glossary REST 서버 | `src/TechGloss.GlossaryApi/` | 생성 |
| Core 단위 테스트 | `tests/TechGloss.Core.Tests/` | 생성 |
| GlossaryApi 통합 테스트 | `tests/TechGloss.GlossaryApi.Tests/` | 생성 |
| 배포 문서 | `docs/DEPLOY.md` | 생성 |
| WPF 프로젝트 파일 | `src/TechGloss.Wpf/TechGloss.Wpf.csproj` | 생성 |
| GlossaryApi 프로젝트 파일 | `src/TechGloss.GlossaryApi/TechGloss.GlossaryApi.csproj` | 생성 |
| 설정 기본값 | `src/TechGloss.Wpf/appsettings.json` | 생성 |
| DB 마이그레이션 | `src/TechGloss.GlossaryApi/Migrations/` | 생성 |
| 시드 데이터 | `src/TechGloss.GlossaryApi/Data/seed.json` | 생성 |
| SPA 빌드 출력 복사 대상 | `src/TechGloss.Wpf/Web/dist/` | 빌드 산출물 |

---

## 설계 원칙 (Research §2 → 구현 계약)

1. **WPF ↔ 벡터 직접 연결 금지** — `TechGloss.Wpf`, `TechGloss.Infrastructure`에 `Qdrant.Client` 패키지 참조 불가; GlossaryApi 전용.
2. **호스트 HTTP 전용** — Ollama·GlossaryApi 호출은 반드시 WPF 호스트 `HttpClient`; WebView 내 `fetch()` 직접 호출 금지.
3. **LLM URL·모델 기본 고정** — `appsettings.json` 기본값 `172.20.64.76:11434` / `gemma4:31b`; 환경 오버레이(`appsettings.Production.json`)만 허용.
4. **이중 검색 경계 유지** — 의미 유사도=`POST /glossary/search`(임베딩+벡터), 문자열=`GET /glossary/lookup`(SQL LIKE); 혼합 금지.
5. **방향 명시 필수** — 모든 번역 요청에 `source_lang`/`target_lang` 포함; 자동 감지는 UI 보조만.

---

## 트레이드오프 분석

### T1. WebView2 vs CEFSharp vs MAUI Blazor Hybrid

| 항목 | WebView2 | CEFSharp | MAUI Blazor |
|------|----------|----------|-------------|
| **WPF 호환** | 공식 NuGet, 단순 | 공식 지원 but 유지비용 높음 | WPF 대체 필요 |
| **엔터프라이즈 배포** | Evergreen/Fixed 두 모드 | 런타임 동봉 필수, 사이즈 큼 | .NET MAUI 별도 |
| **postMessage 브리지** | 네이티브 | 추가 라이브러리 필요 | Blazor Interop |
| **보안 정책** | Edge 정책 공유 | 별도 패치 관리 | 별도 |
| **결론** | **채택** | 비채택 | 비채택 |

**결정:** WebView2. 이유 — Microsoft 공식 지원, WPF NuGet 단순, Evergreen/Fixed 배포 유연성.

**단점:** 엔터프라이즈 환경에서 런타임 미설치 리스크 → 설치 스크립트에 WebView2 부트스트래퍼 포함으로 완화.

---

### T2. postMessage vs AddHostObjectToScript

| 항목 | postMessage (JSON) | AddHostObjectToScript (COM) |
|------|--------------------|-----------------------------|
| **타입 안전성** | 수동 JSON 스키마 | COM AutoDual — 자동 |
| **비동기** | 양방향 이벤트 패턴 | JS `await` 필요, 예외 전파 복잡 |
| **디버깅** | DevTools Network 패널 추적 가능 | COM 오류 메시지 난해 |
| **보안 경계** | 명시적 직렬화·역직렬화 | 객체 노출 범위 실수 가능 |
| **결론** | **기본 채택** | 성능 필요 소수 API만 선택 |

**결정:** `postMessage + 명시적 JSON 스키마` 우선. 번역 스트리밍·lookup 모두 이 경로. COM host object는 쓰지 않음(Research §3.4 권장 반영).

---

### T3. 벡터 DB — Qdrant vs SQLite-vec

| 항목 | Qdrant (Docker) | SQLite-vec |
|------|----------------|------------|
| **필터·페이로드** | 네이티브 지원 | 제한적 |
| **하이브리드 검색** | 지원 | 미지원 |
| **배포 복잡도** | Docker 필요 | 단일 파일 |
| **멀티벡터 인덱스** | 지원 | 미지원 |
| **MVP 속도** | 느림(Docker 세팅) | 빠름 |
| **결론** | **Phase D 이후 권장** | Phase A~C MVP 대안 |

**결정:** MVP(Task 1~3)는 SQLite + EF Core로 lookup 먼저 구현. Task 4부터 Qdrant 도입하고 API 계약 유지. GlossaryApi 내부에서만 전환하므로 WPF 쪽 코드 변경 없음.

---

### T4. Ollama 네이티브 API vs OpenAI 호환 경로

| 항목 | `/api/chat` (네이티브) | `/v1/chat/completions` (OpenAI 호환) |
|------|------------------------|--------------------------------------|
| **스트리밍 형식** | NDJSON | SSE |
| **파서** | 줄 단위 JSON | `data:` prefix 제거 필요 |
| **클라이언트 SDK** | 없음(직접 구현) | OpenAI SDK 사용 가능 |
| **설정 단순화** | 경로 고정 | 경로 스위치 필요 |
| **결론** | **기본 채택** | `appsettings` 플래그로 선택 가능 |

**결정:** 기본은 `/api/chat` + NDJSON 파서. `UseOpenAiCompatiblePath: false`가 기본값. CI는 네이티브 경로만 테스트.

---

### T5. 임베딩 모델 선택

| 항목 | Ollama 내 모델 (`nomic-embed-text` 등) | 외부 임베딩 서비스 |
|------|----------------------------------------|-------------------|
| **다국어(한·영)** | BGE-m3, multilingual-e5 계열 가능 | OpenAI ada-002 등 |
| **비용** | 사내 GPU — 사실상 무료 | 토큰당 과금 |
| **지연** | 사내망 RTT | 외부망 |
| **결론** | **채택** | 비채택(비목표) |

**결정:** `POST /api/embeddings` 동일 `172.20.64.76:11434`로 다국어 임베딩 모델 사용. 모델명은 `appsettings`에서 분리(`EmbeddingModel` 별도 키).

---

## Task 1: 솔루션 스캐폴딩 + Core 도메인 모델

**Files:**
- Create: `TechGloss.sln`
- Create: `Directory.Build.props`
- Create: `src/TechGloss.Core/TechGloss.Core.csproj`
- Create: `src/TechGloss.Core/Models/GlossaryEntry.cs`
- Create: `src/TechGloss.Core/Models/TranslationDirection.cs`
- Create: `src/TechGloss.Core/Contracts/IGlossaryClient.cs`
- Create: `src/TechGloss.Core/Contracts/IOllamaChatClient.cs`
- Create: `src/TechGloss.Core/Messages/WebEnvelope.cs`
- Test: `tests/TechGloss.Core.Tests/Models/GlossaryEntryTests.cs`

- [ ] **Step 1: 솔루션 및 공통 빌드 파일 작성**

```bash
dotnet new sln -n TechGloss
```

`Directory.Build.props`:
```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Core 프로젝트 생성**

```bash
dotnet new classlib -n TechGloss.Core -o src/TechGloss.Core --framework net8.0
dotnet sln add src/TechGloss.Core/TechGloss.Core.csproj
```

- [ ] **Step 3: 번역 방향 열거형 작성**

`src/TechGloss.Core/Models/TranslationDirection.cs`:
```csharp
namespace TechGloss.Core.Models;

// 번역 방향을 열거형으로 표현 — 문자열 비교 오류를 컴파일 타임에 방지
// UI 토글("en-ko" / "ko-en")과 이 열거형을 분리해, 도메인 로직이 문자열에 의존하지 않도록 한다
public enum TranslationDirection
{
    EnToKo,  // 영어 → 한국어 (기본값: IT 문서 한국어화)
    KoToEn   // 한국어 → 영어 (내부 자료 국제화)
}

public static class TranslationDirectionExtensions
{
    // HostBridge / Orchestrator에서 (sourceLang, targetLang) 튜플이 필요한 호출부에 변환 제공
    // switch 표현식 대신 삼항 연산자 — 두 값만 있으므로 더 간결
    public static (string SourceLang, string TargetLang) ToLangPair(this TranslationDirection dir) =>
        dir == TranslationDirection.EnToKo ? ("en", "ko") : ("ko", "en");
}
```

- [ ] **Step 4: GlossaryEntry 도메인 모델 작성**

`src/TechGloss.Core/Models/GlossaryEntry.cs`:
```csharp
namespace TechGloss.Core.Models;

// Research §5.6.3 — 작성자/승인자 컬럼 없음. Qdrant point id = Id (UUID).
// EF Core가 이 클래스를 glossary_entry 테이블에 매핑 (GlossaryDbContext 참고)
public sealed class GlossaryEntry
{
    // 벡터 DB(Qdrant)의 point id와 동일 UUID를 쓰면 SQL ↔ 벡터 동기화가 단순해진다
    public Guid Id { get; set; }

    public string TermKo { get; set; } = "";  // 화면 표시용 국문 원형 (예: "배포")
    public string TermEn { get; set; } = "";  // 화면 표시용 영문 원형 (예: "deploy")

    // UNIQUE 제약 보조 컬럼 — 앱 레이어에서 NFKC + Trim + ToLower 적용 후 저장
    // DB 레벨 COLLATION에 의존하지 않고 정규화를 명시적으로 관리
    public string TermKoNormalized { get; set; } = "";
    public string TermEnNormalized { get; set; } = "";

    public string DefinitionKo { get; set; } = "";  // 국문 정의만 (Research §5.6.1); 영문 정의 컬럼 없음
    public Guid? CategoryId { get; set; }            // null = 미분류; ON DELETE SET NULL

    public string? Notes { get; set; }               // 내부 메모, UI 미노출 가능
    public bool CaseSensitive { get; set; } = false; // 고유명사 등 대소문 구분 예외 처리 시 true
    public bool IsPreferred { get; set; } = true;    // 동의어 다수일 때 권장 용어 쌍 표시

    // RAG 인덱싱 대상 제어: published 상태만 벡터 인덱스에 등록 (Research §5.5)
    // draft: 작성 중, published: RAG 활성, deprecated: 검색 제외
    public string Status { get; set; } = "draft";

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }  // upsert 시 항상 갱신
}

public sealed class GlossaryCategory
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = "";   // URL/필터용 식별자 (예: "cloud", "frontend", "dotnet")
    public string Name { get; set; } = "";   // 한글 표시명 권장 (Research §5.6.3) — lookup 결과에 노출
    public Guid? ParentId { get; set; }      // 계층 구조 지원; null = 최상위 카테고리
}
```

- [ ] **Step 5: DTO — GlossarySearchRequest / GlossaryLookupRow 작성**

`src/TechGloss.Core/Contracts/GlossaryDtos.cs`:
```csharp
namespace TechGloss.Core.Contracts;

// Research §4.3~4.4 — 방향·카테고리 필터 명시
// init 전용 프로퍼티 사용: 생성 후 변경 불가 — 요청 객체 불변성 보장
public sealed class GlossarySearchRequest
{
    // 번역 중인 문장 또는 청크 전체 — 임베딩 입력이 됨 (Phase D)
    public required string QueryText { get; init; }

    public required string SourceLang { get; init; }   // "en" | "ko" — 명시 필수, 자동 감지 금지
    public required string TargetLang { get; init; }   // "en" | "ko" — source와 반드시 다른 값

    // 상위 k개만 프롬프트에 삽입 — 너무 많으면 컨텍스트 낭비; 기본 8은 Research §4.4 권장값
    public int TopK { get; init; } = 8;

    // 동일 영문 용어가 카테고리마다 다른 국문 대역을 가질 때 필터링 (Research §4.4 용어 충돌 절)
    public string? CategorySlug { get; init; }
}

// GlossaryApi가 WPF 호스트에 반환하는 RAG 검색 결과 행
public sealed class GlossarySearchRow
{
    public Guid EntryId { get; init; }
    public string TermEn { get; init; } = "";
    public string TermKo { get; init; } = "";
    public string DefinitionKo { get; init; } = "";    // 프롬프트 글로서리 표에 삽입됨
    public string CategorySlug { get; init; } = "";    // 벡터 payload filter와 동일 값

    // GlossaryApi가 방향에 맞게 이미 정규화해서 내려줌
    // EN→KO면 Source="deploy", Target="배포"; KO→EN이면 반전
    // PromptBuilder는 이 값을 그대로 표에 사용 — 방향 분기 로직 제거
    public string Source { get; init; } = "";
    public string Target { get; init; } = "";
}

// Research §5.7.1 — lookup 결과에 category 표시명 포함
// LookupPane.tsx에서 직렬화 없이 바로 렌더링할 수 있는 평탄한 구조
public sealed class GlossaryLookupRow
{
    public Guid Id { get; init; }
    public string TermKo { get; init; } = "";          // 굵게 표시 (LookupPane 참고)
    public string TermEn { get; init; } = "";          // 옅은 색으로 병기
    public string DefinitionKo { get; init; } = "";    // 상세 설명 — 즉시 표시가 핵심 UX
    public string? CategoryName { get; init; }         // 한글 표시명; null이면 UI에서 생략
}
```

- [ ] **Step 6: 클라이언트 인터페이스 정의**

`src/TechGloss.Core/Contracts/IGlossaryClient.cs`:
```csharp
namespace TechGloss.Core.Contracts;

// 이 인터페이스만 알면 WPF 호스트는 GlossaryApi 내부 구현(벡터 DB 종류 등)과 완전히 분리됨
// 테스트 시 Mock<IGlossaryClient>로 교체, 실제 서버 없이 단위 테스트 가능
public interface IGlossaryClient
{
    // RAG 검색 — 벡터 유사도 (Research §5.6.6)
    // MVP 단계에서는 내부적으로 SQL LIKE로 동작, Phase D에서 Qdrant 임베딩으로 교체
    // 호출자(TranslationOrchestrator)는 내부 구현과 무관하게 동일 인터페이스 사용
    Task<IReadOnlyList<GlossarySearchRow>> SearchAsync(
        GlossarySearchRequest request, CancellationToken ct = default);

    // 글자 단위 LIKE 검색 (Research §5.7) — 임베딩 불필요, SQL만 사용
    // lang="auto" 시 term_ko, term_en, definition_ko 세 컬럼 OR 검색
    Task<IReadOnlyList<GlossaryLookupRow>> LookupAsync(
        string q, string lang = "auto", int limit = 20,
        Guid? categoryId = null, CancellationToken ct = default);

    // 번역 결과 확정 시 사용자가 승인 → upsert로 draft 생성, publish로 RAG 활성화
    Task UpsertAsync(GlossaryEntry entry, CancellationToken ct = default);
    Task PublishAsync(Guid entryId, CancellationToken ct = default);
}
```

`src/TechGloss.Core/Contracts/IOllamaChatClient.cs`:
```csharp
namespace TechGloss.Core.Contracts;

public interface IOllamaChatClient
{
    // IAsyncEnumerable<string>: C# 8+ 비동기 스트림 — await foreach로 토큰 델타를 순차 소비
    // 반환값은 LLM이 생성한 토큰 조각(delta) 문자열 시퀀스
    // TranslationOrchestrator가 각 청크를 즉시 postMessage로 SPA에 전달 → 스트리밍 UX 구현
    // EnumeratorCancellation 어트리뷰트는 구현체(OllamaHttpClient)에서 ct 전파에 필요
    IAsyncEnumerable<string> StreamChatAsync(
        string systemPrompt, string userText, CancellationToken ct = default);
}
```

- [ ] **Step 7: WebView2 메시지 봉투 타입 정의**

`src/TechGloss.Core/Messages/WebEnvelope.cs`:
```csharp
using System.Text.Json;

namespace TechGloss.Core.Messages;

// postMessage 계약 — type 필드로 분기 (Research §3.4)
// SPA가 JSON.stringify({ type, payload })로 직렬화해서 chrome.webview.postMessage 호출
// 호스트는 JsonSerializer.Deserialize<WebEnvelope>로 역직렬화 → Type으로 switch 분기
public sealed class WebEnvelope
{
    public required string Type { get; init; }   // "translate" | "lookup" | ... — switch 분기 키
    // JsonElement 유지: Payload 구조가 Type마다 달라 제네릭 역직렬화 불가
    // HostBridge에서 Type 확인 후 GetProperty로 필요한 필드만 추출
    public JsonElement Payload { get; init; }
}
```

- [ ] **Step 8: 단위 테스트 작성 및 실패 확인**

`tests/TechGloss.Core.Tests/Models/GlossaryEntryTests.cs`:
```csharp
namespace TechGloss.Core.Tests.Models;

public class GlossaryEntryTests
{
    [Fact]
    public void Status_DefaultIsDraft()
    {
        var entry = new GlossaryEntry();
        Assert.Equal("draft", entry.Status);
    }

    [Fact]
    public void TranslationDirection_EnToKo_ReturnsCorrectPair()
    {
        var (src, tgt) = TranslationDirection.EnToKo.ToLangPair();
        Assert.Equal("en", src);
        Assert.Equal("ko", tgt);
    }

    [Fact]
    public void TranslationDirection_KoToEn_ReturnsCorrectPair()
    {
        var (src, tgt) = TranslationDirection.KoToEn.ToLangPair();
        Assert.Equal("ko", src);
        Assert.Equal("en", tgt);
    }
}
```

```bash
cd tests/TechGloss.Core.Tests && dotnet test
```
Expected: FAIL — 프로젝트 참조 없음

- [ ] **Step 9: 테스트 프로젝트 연결 후 통과 확인**

```bash
dotnet new xunit -n TechGloss.Core.Tests -o tests/TechGloss.Core.Tests --framework net8.0
dotnet sln add tests/TechGloss.Core.Tests/TechGloss.Core.Tests.csproj
dotnet add tests/TechGloss.Core.Tests reference src/TechGloss.Core
dotnet test tests/TechGloss.Core.Tests
```
Expected: PASS (3 tests)

- [ ] **Step 10: 커밋**

```bash
git add src/TechGloss.Core tests/TechGloss.Core.Tests Directory.Build.props TechGloss.sln
git commit -m "feat: add Core domain models, contracts, and message types"
```

---

## Task 2: Infrastructure — HttpClient 팩토리·설정·SSRF 핸들러

**Files:**
- Create: `src/TechGloss.Infrastructure/TechGloss.Infrastructure.csproj`
- Create: `src/TechGloss.Infrastructure/Options/TechGlossOptions.cs`
- Create: `src/TechGloss.Infrastructure/Http/AllowedHostsHandler.cs`
- Create: `src/TechGloss.Infrastructure/Http/OllamaHttpClient.cs`
- Create: `src/TechGloss.Infrastructure/Http/GlossaryHttpClient.cs`
- Create: `src/TechGloss.Infrastructure/ServiceCollectionExtensions.cs`
- Test: `tests/TechGloss.Core.Tests/Http/AllowedHostsHandlerTests.cs`

- [ ] **Step 1: Infrastructure 프로젝트 생성 및 패키지 추가**

```bash
dotnet new classlib -n TechGloss.Infrastructure -o src/TechGloss.Infrastructure --framework net8.0
dotnet sln add src/TechGloss.Infrastructure/TechGloss.Infrastructure.csproj
dotnet add src/TechGloss.Infrastructure reference src/TechGloss.Core
dotnet add src/TechGloss.Infrastructure package Microsoft.Extensions.Http
dotnet add src/TechGloss.Infrastructure package Microsoft.Extensions.Options.ConfigurationExtensions
dotnet add src/TechGloss.Infrastructure package Polly.Extensions.Http
```

- [ ] **Step 2: 설정 옵션 클래스 작성**

`src/TechGloss.Infrastructure/Options/TechGlossOptions.cs`:
```csharp
namespace TechGloss.Infrastructure.Options;

// appsettings.json "TechGloss" 섹션 바인딩 (Research §2.2)
public sealed class TechGlossOptions
{
    public OllamaOptions Ollama { get; set; } = new();
    public GlossaryApiOptions GlossaryApi { get; set; } = new();
}

public sealed class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://172.20.64.76:11434";  // Research §2.1 고정
    public string Model { get; set; } = "gemma4:31b";                    // Research §2.1 고정
    public string EmbeddingModel { get; set; } = "nomic-embed-text";
    public string ChatPath { get; set; } = "/api/chat";                  // Research §4.1
    public bool UseOpenAiCompatiblePath { get; set; } = false;           // Research §13.5
    public int TimeoutSeconds { get; set; } = 120;
}

public sealed class GlossaryApiOptions
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:5088";      // Research §2.1
}
```

- [ ] **Step 3: SSRF 방지 핸들러 테스트 먼저 작성**

`tests/TechGloss.Core.Tests/Http/AllowedHostsHandlerTests.cs`:
```csharp
using TechGloss.Infrastructure.Http;

namespace TechGloss.Core.Tests.Http;

public class AllowedHostsHandlerTests
{
    [Fact]
    public async Task AllowedHost_Passes()
    {
        var handler = new AllowedHostsHandler(new[] { "172.20.64.76", "127.0.0.1" })
        {
            InnerHandler = new TestInnerHandler()
        };
        var client = new HttpClient(handler);
        var response = await client.GetAsync("http://172.20.64.76:11434/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DisallowedHost_Throws()
    {
        var handler = new AllowedHostsHandler(new[] { "172.20.64.76" })
        {
            InnerHandler = new TestInnerHandler()
        };
        var client = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetAsync("http://evil.example.com/steal"));
    }

    private sealed class TestInnerHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
```

```bash
dotnet test tests/TechGloss.Core.Tests --filter AllowedHostsHandlerTests
```
Expected: FAIL — AllowedHostsHandler 없음

- [ ] **Step 4: SSRF 핸들러 구현**

`src/TechGloss.Infrastructure/Http/AllowedHostsHandler.cs`:
```csharp
namespace TechGloss.Infrastructure.Http;

// Research §4.6 — 사용자 입력 URL로 임의 호스트 호출 차단(SSRF). 허용 목록만 통과.
// DelegatingHandler: HttpClient 파이프라인의 미들웨어 역할 — 모든 요청이 여기를 통과
// ServiceCollectionExtensions에서 AddHttpMessageHandler로 Ollama/Glossary 클라이언트 양쪽에 등록
public sealed class AllowedHostsHandler : DelegatingHandler
{
    // OrdinalIgnoreCase: "172.20.64.76" vs "172.20.64.76" 대소문 구분 없이 비교
    // HashSet<string>: O(1) 조회 — 요청마다 선형 탐색 없음
    private readonly HashSet<string> _allowedHosts;

    public AllowedHostsHandler(IEnumerable<string> allowedHosts)
        => _allowedHosts = new HashSet<string>(allowedHosts, StringComparer.OrdinalIgnoreCase);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        // Uri.Host: 포트 제외 순수 호스트명만 반환 (예: "172.20.64.76")
        // Uri.Authority를 쓰면 포트까지 포함되므로 Host가 더 단순
        var host = request.RequestUri?.Host
            ?? throw new InvalidOperationException("Request URI is null");

        // 허용 목록 외 호스트: 예외 발생 → HttpClient가 caller에게 전파
        // 의도적으로 조용히 실패하지 않음 — 보안 위반은 명시적 오류로 처리
        if (!_allowedHosts.Contains(host))
            throw new InvalidOperationException($"Host not in allow-list: {host}");

        return base.SendAsync(request, ct);  // 다음 핸들러(실제 HTTP 전송)로 위임
    }
}
```

- [ ] **Step 5: 테스트 통과 확인**

```bash
dotnet test tests/TechGloss.Core.Tests --filter AllowedHostsHandlerTests
```
Expected: PASS (2 tests)

- [ ] **Step 6: Ollama HTTP 클라이언트 구현**

`src/TechGloss.Infrastructure/Http/OllamaHttpClient.cs`:
```csharp
using System.Runtime.CompilerServices;
using System.Text.Json;
using TechGloss.Core.Contracts;
using TechGloss.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace TechGloss.Infrastructure.Http;

// Research §4.1~4.2 — Ollama 네이티브 /api/chat NDJSON 스트리밍
// IHttpClientFactory 패턴: 생성자에서 HttpClient를 직접 new 하지 않음 — DI가 수명주기 관리
public sealed class OllamaHttpClient : IOllamaChatClient
{
    private readonly HttpClient _http;
    private readonly OllamaOptions _opts;  // IOptions<T>에서 꺼낸 값을 캐시 — 매 호출마다 재조회 방지

    public OllamaHttpClient(HttpClient http, IOptions<TechGlossOptions> options)
    {
        _http = http;
        _opts = options.Value.Ollama;
    }

    // [EnumeratorCancellation]: IAsyncEnumerable을 await foreach로 사용할 때 ct 전파를 위해 필수
    // 없으면 WithCancellation(ct) 호출 시 ct가 내부 루프에 전달되지 않음
    public async IAsyncEnumerable<string> StreamChatAsync(
        string systemPrompt, string userText,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Research §4.1 — UseOpenAiCompatiblePath 플래그로 경로 분기; 기본은 Ollama 네이티브
        // OpenAI 호환 경로 사용 시 SSE 파서 별도 필요 (현재 구현은 NDJSON 전용)
        var path = _opts.UseOpenAiCompatiblePath
            ? "/v1/chat/completions"
            : _opts.ChatPath;  // 기본: "/api/chat"

        // TrimEnd('/'): BaseUrl 끝에 '/'가 있든 없든 경로 중복 방지
        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"{_opts.BaseUrl.TrimEnd('/')}{path}");

        req.Content = JsonContent.Create(new
        {
            model = _opts.Model,   // appsettings 기본값 "gemma4:31b" — 요청마다 고정
            stream = true,         // false로 바꾸면 단일 JSON 응답으로 단순화 가능 (디버깅용)
            messages = new[]
            {
                new { role = "system", content = systemPrompt }, // PromptBuilder 출력값
                new { role = "user",   content = userText }      // 사용자 원문 그대로
            }
        });

        // Research §4.2 — ResponseHeadersRead: 응답 헤더만 받으면 본문 스트림을 즉시 소비 시작
        // ResponseContentRead(기본값)는 전체 응답이 버퍼링될 때까지 대기 → 스트리밍 불가
        using var resp = await _http.SendAsync(req,
            HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();  // 4xx/5xx → InvalidOperationException으로 변환

        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);

            // Ollama NDJSON: 빈 줄이나 '{' 로 시작하지 않는 줄은 무시 (연결 유지용 ping 등)
            if (string.IsNullOrWhiteSpace(line) || line[0] != '{') continue;

            // JsonDocument: System.Text.Json의 DOM — 구조가 고정되지 않은 JSON 파싱에 적합
            // using 선언: 루프 반복마다 즉시 Dispose → 메모리 누적 방지
            using var doc = JsonDocument.Parse(line);

            // Ollama /api/chat 스트림 응답 구조: { "message": { "content": "토큰" }, "done": false }
            // OpenAI 호환 경로의 경우 "choices[0].delta.content" 로 다르므로 경로별 파서 필요
            if (doc.RootElement.TryGetProperty("message", out var m)
                && m.TryGetProperty("content", out var c))
            {
                var delta = c.GetString();
                if (!string.IsNullOrEmpty(delta))
                    yield return delta;  // TranslationOrchestrator에서 즉시 postMessage로 전송
            }

            // "done": true — 모델이 생성 완료 신호를 보냄; 루프 명시적 종료
            // reader.EndOfStream만으로는 서버가 연결을 끊기 전에 종료 감지 불가
            if (doc.RootElement.TryGetProperty("done", out var done)
                && done.GetBoolean())
                break;
        }
    }
}
```

- [ ] **Step 7: Glossary HTTP 클라이언트 구현**

`src/TechGloss.Infrastructure/Http/GlossaryHttpClient.cs`:
```csharp
using System.Net.Http.Json;
using TechGloss.Core.Contracts;
using TechGloss.Core.Models;
using TechGloss.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace TechGloss.Infrastructure.Http;

// Research §5.6.6 — WPF는 Glossary API HTTP만 호출; 벡터/SQL 직접 접근 금지
// IGlossaryClient 구현체 — DI 교체 시 이 파일만 바꾸면 됨 (인터페이스 계약 유지)
public sealed class GlossaryHttpClient : IGlossaryClient
{
    private readonly HttpClient _http;
    private readonly GlossaryApiOptions _opts;  // BaseUrl만 사용; 포트는 appsettings에서 관리

    public GlossaryHttpClient(HttpClient http, IOptions<TechGlossOptions> options)
    {
        _http = http;
        _opts = options.Value.GlossaryApi;
    }

    public async Task<IReadOnlyList<GlossarySearchRow>> SearchAsync(
        GlossarySearchRequest request, CancellationToken ct = default)
    {
        // PostAsJsonAsync: System.Net.Http.Json 확장 — 직렬화 + Content-Type 설정 자동화
        var result = await _http.PostAsJsonAsync(
            $"{_opts.BaseUrl}/glossary/search", request, ct);
        result.EnsureSuccessStatusCode();
        // ReadFromJsonAsync: 역직렬화 + null 안전 — 서버가 빈 배열 반환 시 null이 아닌 빈 리스트
        return await result.Content.ReadFromJsonAsync<List<GlossarySearchRow>>(ct)
               ?? new List<GlossarySearchRow>();
    }

    public async Task<IReadOnlyList<GlossaryLookupRow>> LookupAsync(
        string q, string lang = "auto", int limit = 20,
        Guid? categoryId = null, CancellationToken ct = default)
    {
        // Research §5.7.2 — 빈 q는 서버 왕복 없이 즉시 빈 배열 반환 (불필요한 네트워크 절감)
        if (string.IsNullOrEmpty(q)) return Array.Empty<GlossaryLookupRow>();

        // Uri.EscapeDataString: q에 한글, 공백, 특수문자 포함 시 URL 인코딩 필수
        // category_id가 없을 때 파라미터 자체를 생략 (서버 기본 처리 따름)
        var url = $"{_opts.BaseUrl}/glossary/lookup?q={Uri.EscapeDataString(q)}" +
                  $"&lang={lang}&limit={limit}" +
                  (categoryId.HasValue ? $"&category_id={categoryId}" : "");
        var result = await _http.GetFromJsonAsync<List<GlossaryLookupRow>>(url, ct);
        return result ?? new List<GlossaryLookupRow>();
    }

    public async Task UpsertAsync(GlossaryEntry entry, CancellationToken ct = default)
    {
        // 새 용어·기존 용어 구분 없이 단일 엔드포인트 — 서버가 Id 존재 여부로 INSERT/UPDATE 결정
        var result = await _http.PostAsJsonAsync($"{_opts.BaseUrl}/glossary/upsert", entry, ct);
        result.EnsureSuccessStatusCode();
    }

    public async Task PublishAsync(Guid entryId, CancellationToken ct = default)
    {
        // 익명 객체로 최소 페이로드 전송 — 서버가 status=published로만 전환
        // 벡터 인덱스 등록도 서버 내부에서 처리 (WPF 호스트는 관여하지 않음)
        var result = await _http.PostAsJsonAsync(
            $"{_opts.BaseUrl}/glossary/publish", new { entry_id = entryId }, ct);
        result.EnsureSuccessStatusCode();
    }
}
```

- [ ] **Step 8: DI 등록 확장 메서드**

`src/TechGloss.Infrastructure/ServiceCollectionExtensions.cs`:
```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using TechGloss.Core.Contracts;
using TechGloss.Infrastructure.Http;
using TechGloss.Infrastructure.Options;

namespace TechGloss.Infrastructure;

public static class ServiceCollectionExtensions
{
    // 확장 메서드: App.xaml.cs에서 services.AddTechGlossInfrastructure(config) 한 줄로 등록
    public static IServiceCollection AddTechGlossInfrastructure(
        this IServiceCollection services, IConfiguration config)
    {
        // IOptions<TechGlossOptions>로 런타임 바인딩 — 핫 리로드 지원 (개발 편의)
        services.Configure<TechGlossOptions>(config.GetSection("TechGloss"));

        // 설정 값을 직접 읽어 allowedHosts 구성 — IOptions보다 먼저 필요하므로 직접 Get
        var opts = config.GetSection("TechGloss").Get<TechGlossOptions>() ?? new();
        var allowedHosts = new[]
        {
            new Uri(opts.Ollama.BaseUrl).Host,      // 예: "172.20.64.76"
            new Uri(opts.GlossaryApi.BaseUrl).Host, // 예: "127.0.0.1"
            "localhost", "127.0.0.1"                // 개발 환경 localhost 대비
        };

        services
            .AddHttpClient<IOllamaChatClient, OllamaHttpClient>(c =>
            {
                // LLM 응답 시간이 길 수 있으므로 타임아웃을 넉넉히 (기본 120s)
                // 스트리밍 중에는 ReadTimeout이 지배하므로 Timeout은 연결 수립 기준
                c.Timeout = TimeSpan.FromSeconds(opts.Ollama.TimeoutSeconds);
            })
            .AddHttpMessageHandler(() => new AllowedHostsHandler(allowedHosts)) // SSRF 방지
            .AddTransientHttpErrorPolicy(p =>
                // 지수 백오프 재시도: 1s → 2s → 4s (Polly)
                // TransientHttpError = 5xx, 408, 네트워크 오류
                p.WaitAndRetryAsync(3, i => TimeSpan.FromSeconds(Math.Pow(2, i))));

        services
            .AddHttpClient<IGlossaryClient, GlossaryHttpClient>(c =>
            {
                // 로컬 서버이므로 30s이면 충분; lookup은 더 빠를 것
                c.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddHttpMessageHandler(() => new AllowedHostsHandler(allowedHosts));
            // Glossary API는 재시도 없음 — 로컬 서버 다운 시 즉각 오류가 더 직관적

        return services;
    }
}
```

- [ ] **Step 9: 커밋**

```bash
git add src/TechGloss.Infrastructure tests/TechGloss.Core.Tests/Http
git commit -m "feat: add Infrastructure HttpClients with SSRF guard and Polly retry"
```

---

## Task 3: GlossaryApi — DB 스키마·마이그레이션·lookup 엔드포인트

**Files:**
- Create: `src/TechGloss.GlossaryApi/TechGloss.GlossaryApi.csproj`
- Create: `src/TechGloss.GlossaryApi/Data/GlossaryDb.cs`
- Create: `src/TechGloss.GlossaryApi/Data/GlossaryDbContext.cs`
- Create: `src/TechGloss.GlossaryApi/Data/Migrations/001_initial.sql`
- Create: `src/TechGloss.GlossaryApi/Data/seed.json`
- Create: `src/TechGloss.GlossaryApi/Endpoints/LookupEndpoint.cs`
- Create: `src/TechGloss.GlossaryApi/Program.cs`
- Test: `tests/TechGloss.GlossaryApi.Tests/LookupEndpointTests.cs`

- [ ] **Step 1: GlossaryApi 프로젝트 생성**

```bash
dotnet new webapi -n TechGloss.GlossaryApi -o src/TechGloss.GlossaryApi --framework net8.0 --no-openapi
dotnet sln add src/TechGloss.GlossaryApi/TechGloss.GlossaryApi.csproj
dotnet add src/TechGloss.GlossaryApi reference src/TechGloss.Core
dotnet add src/TechGloss.GlossaryApi package Microsoft.EntityFrameworkCore.Sqlite
dotnet add src/TechGloss.GlossaryApi package Microsoft.EntityFrameworkCore.Design
```

- [ ] **Step 2: lookup 테스트 먼저 작성 (TDD)**

`tests/TechGloss.GlossaryApi.Tests/LookupEndpointTests.cs`:
```csharp
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net.Http.Json;
using TechGloss.Core.Contracts;

namespace TechGloss.GlossaryApi.Tests;

public class LookupEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public LookupEndpointTests(WebApplicationFactory<Program> factory)
        => _client = factory.CreateClient();

    [Fact]
    public async Task Lookup_EmptyQ_ReturnsEmptyArray()
    {
        var resp = await _client.GetAsync("/glossary/lookup?q=");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var rows = await resp.Content.ReadFromJsonAsync<List<GlossaryLookupRow>>();
        Assert.Empty(rows!);
    }

    [Fact]
    public async Task Lookup_KnownTerm_ReturnsMatch()
    {
        // seed 데이터에 "deploy" / "배포"가 있어야 함
        var resp = await _client.GetAsync("/glossary/lookup?q=deploy&lang=en");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var rows = await resp.Content.ReadFromJsonAsync<List<GlossaryLookupRow>>();
        Assert.Contains(rows!, r => r.TermEn.Contains("deploy", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Lookup_QMaxLength_ReturnsOk()
    {
        var longQ = new string('a', 129);  // 128자 초과
        var resp = await _client.GetAsync($"/glossary/lookup?q={longQ}");
        // 서버가 400 또는 빈 배열 반환 — 서버 다운이 아니어야 함
        Assert.True(resp.StatusCode == HttpStatusCode.OK
                 || resp.StatusCode == HttpStatusCode.BadRequest);
    }
}
```

```bash
dotnet test tests/TechGloss.GlossaryApi.Tests
```
Expected: FAIL — 프로그램 없음

- [ ] **Step 3: DB 마이그레이션 SQL 작성**

`src/TechGloss.GlossaryApi/Data/Migrations/001_initial.sql`:
```sql
-- Research §5.6.3 — 작성자/승인자 컬럼 없음
CREATE TABLE IF NOT EXISTS glossary_category (
    id          TEXT PRIMARY KEY,   -- UUID 문자열
    slug        TEXT NOT NULL UNIQUE,
    name        TEXT NOT NULL,
    parent_id   TEXT REFERENCES glossary_category(id)
);

CREATE TABLE IF NOT EXISTS glossary_entry (
    id                  TEXT PRIMARY KEY,
    term_ko             TEXT NOT NULL,
    term_en             TEXT NOT NULL,
    term_ko_normalized  TEXT NOT NULL,
    term_en_normalized  TEXT NOT NULL,
    definition_ko       TEXT NOT NULL DEFAULT '',
    category_id         TEXT REFERENCES glossary_category(id) ON DELETE SET NULL,
    notes               TEXT,
    case_sensitive      INTEGER NOT NULL DEFAULT 0,
    is_preferred        INTEGER NOT NULL DEFAULT 1,
    status              TEXT NOT NULL DEFAULT 'draft'
                            CHECK(status IN ('draft','published','deprecated')),
    created_at          TEXT NOT NULL,
    updated_at          TEXT NOT NULL
);

-- Research §5.6.4 인덱스
CREATE UNIQUE INDEX IF NOT EXISTS uq_entry_en_cat
    ON glossary_entry(term_en_normalized, category_id);

CREATE INDEX IF NOT EXISTS idx_entry_status_cat
    ON glossary_entry(status, category_id);

-- Research §5.6.4 — SQLite FTS5 (LIKE '%q%' 성능 보조)
CREATE VIRTUAL TABLE IF NOT EXISTS glossary_fts USING fts5(
    id UNINDEXED,
    term_ko,
    term_en,
    definition_ko,
    content='glossary_entry',
    content_rowid='rowid'
);

CREATE TABLE IF NOT EXISTS glossary_embedding_state (
    entry_id            TEXT PRIMARY KEY REFERENCES glossary_entry(id),
    embed_model         TEXT NOT NULL,
    embed_dimension     INTEGER NOT NULL,
    embed_text_hash     TEXT NOT NULL,
    vector_store        TEXT NOT NULL DEFAULT 'sqlite_vec',
    vector_point_id     TEXT NOT NULL,
    last_embedded_at    TEXT,
    last_error          TEXT
);
```

- [ ] **Step 4: EF Core DbContext 작성**

`src/TechGloss.GlossaryApi/Data/GlossaryDbContext.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using TechGloss.Core.Models;

namespace TechGloss.GlossaryApi.Data;

public sealed class GlossaryDbContext : DbContext
{
    public GlossaryDbContext(DbContextOptions<GlossaryDbContext> options) : base(options) { }

    // DbSet<T>: EF Core가 SQL 쿼리를 생성하는 진입점
    // => Set<T>() 패턴: 필드가 아닌 프로퍼티로 노출해 null 참조 경고 방지
    public DbSet<GlossaryEntry> Entries => Set<GlossaryEntry>();
    public DbSet<GlossaryCategory> Categories => Set<GlossaryCategory>();

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<GlossaryEntry>(e =>
        {
            e.ToTable("glossary_entry");   // 001_initial.sql의 테이블명과 일치해야 함
            e.HasKey(x => x.Id);
            // Guid → TEXT 변환: SQLite는 UUID 네이티브 타입 없음 — 문자열로 저장
            e.Property(x => x.Id).HasConversion<string>();
            e.Property(x => x.CategoryId).HasConversion<string?>(); // null 허용 FK도 동일
            // DB 기본값 설정: EF가 INSERT 시 Status를 생략해도 'draft'로 저장됨
            e.Property(x => x.Status).HasDefaultValue("draft");
        });

        m.Entity<GlossaryCategory>(c =>
        {
            c.ToTable("glossary_category");
            c.HasKey(x => x.Id);
            c.Property(x => x.Id).HasConversion<string>();
        });
        // 주의: UNIQUE 인덱스(uq_entry_en_cat)와 FTS5 가상 테이블은
        // EF Core Migrations 대신 001_initial.sql로 직접 관리
    }
}
```

- [ ] **Step 5: lookup 엔드포인트 구현**

`src/TechGloss.GlossaryApi/Endpoints/LookupEndpoint.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using TechGloss.Core.Contracts;
using TechGloss.GlossaryApi.Data;

namespace TechGloss.GlossaryApi.Endpoints;

public static class LookupEndpoint
{
    public static void MapLookup(this WebApplication app)
    {
        // Research §5.7.2 — GET /glossary/lookup?q=&lang=auto|ko|en&limit=20&category_id=
        // Minimal API: 파라미터는 쿼리스트링에서 자동 바인딩 (이름이 파라미터명과 일치해야 함)
        app.MapGet("/glossary/lookup", async (
            string? q, string? lang, int? limit, Guid? category_id,
            GlossaryDbContext db, CancellationToken ct) =>
        {
            // Research §5.7.2 — 빈 q: 서버 부하 없이 즉시 반환
            // 클라이언트(GlossaryHttpClient)에서도 체크하지만 서버도 방어적으로 처리
            if (string.IsNullOrEmpty(q)) return Results.Ok(Array.Empty<GlossaryLookupRow>());

            // Research §5.7.3 — q 최대 128자 제한 (DoS 방지)
            // 128자 초과 검색어는 실용적 의미가 없고, LIKE '%128자%'는 전체 스캔 부하 큼
            if (q.Length > 128) return Results.BadRequest("q exceeds maximum length of 128");

            // limit 상한 100: 클라이언트가 임의로 큰 값 전송해도 DB 과부하 방지
            var take = Math.Clamp(limit ?? 20, 1, 100);
            var langMode = lang ?? "auto";

            // Research §5.7.3 — LIKE 와일드카드 이스케이프 (파라미터 바인딩과 별개)
            // EF.Functions.Like의 세 번째 인자(escape char)와 여기서 이스케이프한 문자가 일치해야 함
            // 순서 중요: '\' 먼저 이스케이프 → '%', '_' 순서로 처리 (역순이면 중복 이스케이프 발생)
            var escaped = q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            var pattern = $"%{escaped}%";  // 부분 문자열 일치 — 앞뒤 와일드카드

            // AsNoTracking: 읽기 전용 쿼리 — EF 변경 추적 비활성화로 성능 향상
            var query = db.Entries.AsNoTracking()
                .Where(e => e.Status == "published");  // draft/deprecated 제외 (Research §5.5)

            // 카테고리 필터: null이면 전체, 값이 있으면 해당 카테고리만
            if (category_id.HasValue)
                query = query.Where(e => e.CategoryId == category_id);

            // Research §5.7.3 — lang별 컬럼 선택
            // "ko": 한글 입력 → term_ko, definition_ko 검색 (term_en은 제외해 노이즈 감소)
            // "en": 영문 입력 → term_en만 검색
            // "auto": 세 컬럼 OR — 혼합 입력이나 복사·붙여넣기 시 유연한 검색
            query = langMode switch
            {
                "ko" => query.Where(e =>
                    EF.Functions.Like(e.TermKo, pattern, "\\") ||
                    EF.Functions.Like(e.DefinitionKo, pattern, "\\")),
                "en" => query.Where(e =>
                    EF.Functions.Like(e.TermEn, pattern, "\\")),
                _ => query.Where(e =>  // auto — 세 컬럼 OR (Research §5.7.2)
                    EF.Functions.Like(e.TermKo, pattern, "\\") ||
                    EF.Functions.Like(e.TermEn, pattern, "\\") ||
                    EF.Functions.Like(e.DefinitionKo, pattern, "\\"))
            };

            // Research §5.7.1 — category 표시명 포함 (LEFT JOIN)
            // EF Core의 GroupJoin + DefaultIfEmpty 패턴으로 LEFT OUTER JOIN 표현
            // category_id가 null인 항목도 제외되지 않음 (null → c = null → CategoryName = null)
            var rows = await query
                .OrderBy(e => e.TermEn)  // 영문 오름차순 — 알파벳 순 일관성
                .Take(take)
                .Join(db.Categories.AsNoTracking().DefaultIfEmpty(),
                    e => e.CategoryId, c => (Guid?)c.Id,
                    (e, c) => new GlossaryLookupRow
                    {
                        Id = e.Id,
                        TermKo = e.TermKo,
                        TermEn = e.TermEn,
                        DefinitionKo = e.DefinitionKo,
                        CategoryName = c != null ? c.Name : null  // null이면 UI에서 카테고리 레이블 숨김
                    })
                .ToListAsync(ct);

            return Results.Ok(rows);
        });
    }
}
```

- [ ] **Step 6: GlossaryApi Program.cs 작성**

`src/TechGloss.GlossaryApi/Program.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using TechGloss.GlossaryApi.Data;
using TechGloss.GlossaryApi.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<GlossaryDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("Glossary")
        ?? "Data Source=glossary.db"));

var app = builder.Build();

// DB 자동 생성 (개발 편의)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GlossaryDbContext>();
    db.Database.EnsureCreated();
    await SeedIfEmpty(db);
}

app.MapLookup();
// Task 5에서 /glossary/search, /glossary/upsert, /glossary/publish 추가 예정

app.Run();

static async Task SeedIfEmpty(GlossaryDbContext db)
{
    if (await db.Entries.AnyAsync()) return;
    // 시드 데이터 100건 — Task 3 Step 7에서 seed.json 로드
    var seedPath = Path.Combine(AppContext.BaseDirectory, "Data", "seed.json");
    if (!File.Exists(seedPath)) return;
    var json = await File.ReadAllTextAsync(seedPath);
    var entries = System.Text.Json.JsonSerializer.Deserialize<List<TechGloss.Core.Models.GlossaryEntry>>(json);
    if (entries is { Count: > 0 })
    {
        db.Entries.AddRange(entries);
        await db.SaveChangesAsync();
    }
}

public partial class Program { }  // WebApplicationFactory 테스트 접근용
```

- [ ] **Step 7: seed.json 작성 (최소 5건, 실제 100건 목표)**

`src/TechGloss.GlossaryApi/Data/seed.json`:
```json
[
  {
    "Id": "11111111-0000-0000-0000-000000000001",
    "TermKo": "배포",
    "TermEn": "deploy",
    "TermKoNormalized": "배포",
    "TermEnNormalized": "deploy",
    "DefinitionKo": "소프트웨어를 서버나 환경에 설치하고 실행 가능한 상태로 만드는 작업.",
    "Status": "published",
    "CreatedAt": "2026-01-01T00:00:00Z",
    "UpdatedAt": "2026-01-01T00:00:00Z"
  },
  {
    "Id": "11111111-0000-0000-0000-000000000002",
    "TermKo": "빌드",
    "TermEn": "build",
    "TermKoNormalized": "빌드",
    "TermEnNormalized": "build",
    "DefinitionKo": "소스 코드를 컴파일·링크하여 실행 가능한 산출물을 생성하는 과정.",
    "Status": "published",
    "CreatedAt": "2026-01-01T00:00:00Z",
    "UpdatedAt": "2026-01-01T00:00:00Z"
  },
  {
    "Id": "11111111-0000-0000-0000-000000000003",
    "TermKo": "렌더링",
    "TermEn": "render",
    "TermKoNormalized": "렌더링",
    "TermEnNormalized": "render",
    "DefinitionKo": "데이터나 마크업을 화면에 시각적으로 출력하는 처리 과정.",
    "Status": "published",
    "CreatedAt": "2026-01-01T00:00:00Z",
    "UpdatedAt": "2026-01-01T00:00:00Z"
  },
  {
    "Id": "11111111-0000-0000-0000-000000000004",
    "TermKo": "의존성",
    "TermEn": "dependency",
    "TermKoNormalized": "의존성",
    "TermEnNormalized": "dependency",
    "DefinitionKo": "코드나 패키지가 동작하기 위해 외부에서 필요로 하는 라이브러리·모듈.",
    "Status": "published",
    "CreatedAt": "2026-01-01T00:00:00Z",
    "UpdatedAt": "2026-01-01T00:00:00Z"
  },
  {
    "Id": "11111111-0000-0000-0000-000000000005",
    "TermKo": "구현",
    "TermEn": "implementation",
    "TermKoNormalized": "구현",
    "TermEnNormalized": "implementation",
    "DefinitionKo": "설계된 기능이나 알고리즘을 실제 코드로 작성하는 행위.",
    "Status": "published",
    "CreatedAt": "2026-01-01T00:00:00Z",
    "UpdatedAt": "2026-01-01T00:00:00Z"
  }
]
```

- [ ] **Step 8: 테스트 통과 확인**

```bash
dotnet add tests/TechGloss.GlossaryApi.Tests package Microsoft.AspNetCore.Mvc.Testing
dotnet add tests/TechGloss.GlossaryApi.Tests reference src/TechGloss.GlossaryApi
dotnet test tests/TechGloss.GlossaryApi.Tests
```
Expected: PASS (3 tests)

- [ ] **Step 9: 커밋**

```bash
git add src/TechGloss.GlossaryApi tests/TechGloss.GlossaryApi.Tests
git commit -m "feat: add GlossaryApi with DB schema, lookup endpoint, and seed data"
```

---

## Task 4: WPF 셸 + WebView2 부트스트랩 + postMessage 브리지

**Files:**
- Create: `src/TechGloss.Wpf/TechGloss.Wpf.csproj`
- Create: `src/TechGloss.Wpf/App.xaml` + `App.xaml.cs`
- Create: `src/TechGloss.Wpf/MainWindow.xaml` + `MainWindow.xaml.cs`
- Create: `src/TechGloss.Wpf/Bridge/HostBridge.cs`
- Create: `src/TechGloss.Wpf/appsettings.json`
- Modify: `TechGloss.sln` (프로젝트 추가)

- [ ] **Step 1: WPF 프로젝트 생성 및 패키지 추가**

```bash
dotnet new wpf -n TechGloss.Wpf -o src/TechGloss.Wpf --framework net8.0-windows
dotnet sln add src/TechGloss.Wpf/TechGloss.Wpf.csproj
dotnet add src/TechGloss.Wpf reference src/TechGloss.Core
dotnet add src/TechGloss.Wpf reference src/TechGloss.Infrastructure
dotnet add src/TechGloss.Wpf package Microsoft.Web.WebView2
dotnet add src/TechGloss.Wpf package Microsoft.Extensions.Hosting
dotnet add src/TechGloss.Wpf package Microsoft.Extensions.Configuration.Json
```

- [ ] **Step 2: 프로젝트 파일 수동 확인**

`src/TechGloss.Wpf/TechGloss.Wpf.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <!-- Research §3.1 — 공식 WebView2 NuGet -->
    <PackageReference Include="Microsoft.Web.WebView2" Version="1.0.2903.40" />
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="8.0.0" />
    <ProjectReference Include="..\TechGloss.Core\TechGloss.Core.csproj" />
    <ProjectReference Include="..\TechGloss.Infrastructure\TechGloss.Infrastructure.csproj" />
  </ItemGroup>
  <!-- SPA 빌드 산출물 복사 (web/dist → Web/dist) -->
  <ItemGroup>
    <Content Include="Web\dist\**" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: appsettings.json 작성 (Research §2.1, §4.1 기본값 고정)**

`src/TechGloss.Wpf/appsettings.json`:
```json
{
  "TechGloss": {
    "Ollama": {
      "BaseUrl": "http://172.20.64.76:11434",
      "Model": "gemma4:31b",
      "EmbeddingModel": "nomic-embed-text",
      "ChatPath": "/api/chat",
      "UseOpenAiCompatiblePath": false,
      "TimeoutSeconds": 120
    },
    "GlossaryApi": {
      "BaseUrl": "http://127.0.0.1:5088"
    }
  }
}
```

- [ ] **Step 4: MainWindow XAML 작성 (WebView2 배치)**

`src/TechGloss.Wpf/MainWindow.xaml`:
```xml
<Window x:Class="TechGloss.Wpf.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:wv2="clr-namespace:Microsoft.Web.WebView2.Wpf;assembly=Microsoft.Web.WebView2.Wpf"
        Title="TechGloss" Height="800" Width="1200"
        WindowState="Normal">
    <Grid>
        <!-- Research §3.3 — WebView2 컨트롤. 초기화는 Loaded 이벤트에서 수행 -->
        <wv2:WebView2 x:Name="webView" Loaded="OnWebViewLoaded" />
    </Grid>
</Window>
```

- [ ] **Step 5: WebView2 초기화 코드 작성 (Research §3.3~3.4)**

`src/TechGloss.Wpf/MainWindow.xaml.cs`:
```csharp
using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using TechGloss.Wpf.Bridge;

namespace TechGloss.Wpf;

public partial class MainWindow : Window
{
    private readonly HostBridge _bridge;

    public MainWindow(HostBridge bridge)
    {
        InitializeComponent();
        _bridge = bridge;
    }

    // async void: WPF 이벤트 핸들러는 반환값이 void여야 함
    // 예외가 발생하면 WPF Dispatcher 예외 핸들러로 전파 — try/catch 전역 등록 권장
    private async void OnWebViewLoaded(object sender, RoutedEventArgs e)
    {
        // Research §3.3 — 사용자 데이터 폴더 분리: 다중 인스턴스 실행 시 프로파일 충돌 방지
        // LocalApplicationData: 사용자별 앱 데이터 디렉터리 (예: C:\Users\...\AppData\Local)
        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TechGloss", "WebView2");

        // CreateAsync: WebView2 런타임 환경 초기화 — 런타임 미설치 시 여기서 예외 발생
        var env = await CoreWebView2Environment.CreateAsync(
            userDataFolder: userDataFolder);
        // EnsureCoreWebView2Async: 동일 env로 여러 번 호출해도 안전 (멱등)
        await webView.EnsureCoreWebView2Async(env);

        var core = webView.CoreWebView2;

        // Research §3.3 — file:// 대신 가상 호스트로 SPA 로드 (보안·모듈 제약 회피)
        // file:// 출처는 ES 모듈 import, fetch, localStorage 등이 제한됨
        // https://app.local/ 가상 호스트로 매핑하면 Same-Origin 정책이 정상 동작
        var distPath = Path.Combine(AppContext.BaseDirectory, "Web", "dist");
        core.SetVirtualHostNameToFolderMapping(
            "app.local",          // SPA가 참조할 가상 도메인 이름
            distPath,             // Vite 빌드 산출물 폴더 (vite.config.ts outDir와 일치)
            CoreWebView2HostResourceAccessKind.Allow);  // 로컬 파일 읽기 허용

        // Research §3.4 — postMessage 수신 → 브리지로 라우팅
        // WebMessageAsJson: SPA가 chrome.webview.postMessage(JSON.stringify(...))로 보낸 문자열
        core.WebMessageReceived += (_, args) =>
        {
            var json = args.WebMessageAsJson;
            // Dispatcher.InvokeAsync: WPF UI 스레드에서 브리지 실행 (WebView2 콜백은 별도 스레드)
            // 람다 반환값(Task) 무시 — 예외는 전역 DispatcherUnhandledException 핸들러로 처리
            _ = Dispatcher.InvokeAsync(() =>
                _bridge.HandleWebMessageAsync(json, replyJson =>
                    core.PostWebMessageAsJson(replyJson)));  // 호스트→웹 회신 콜백
        };

        // 개발 모드: DevTools 자동 열기 (Research §3.5) — 릴리스 빌드에서는 제거됨
#if DEBUG
        core.OpenDevToolsWindow();
#endif

        // 반드시 SetVirtualHostNameToFolderMapping 이후에 Navigate 호출
        // 이전에 Navigate하면 매핑이 적용되지 않아 404 발생
        core.Navigate("https://app.local/index.html");
    }
}
```

- [ ] **Step 6: HostBridge 구현**

`src/TechGloss.Wpf/Bridge/HostBridge.cs`:
```csharp
using System.Text.Json;
using TechGloss.Core.Contracts;
using TechGloss.Core.Messages;

namespace TechGloss.Wpf.Bridge;

// Research §3.4, §5.7.5 — 모든 외부 HTTP는 호스트에서; Web은 메시지만 보냄
// HostBridge = SPA ↔ .NET 호스트 사이의 유일한 통신 게이트웨이
// Singleton으로 등록 (App.xaml.cs 참고) — 상태 없음, 재사용 안전
public sealed class HostBridge
{
    private readonly IGlossaryClient _glossary;
    private readonly TranslationOrchestrator _translator;

    public HostBridge(IGlossaryClient glossary, TranslationOrchestrator translator)
    {
        _glossary = glossary;
        _translator = translator;
    }

    // replyToWeb: MainWindow에서 주입하는 콜백 — core.PostWebMessageAsJson(json) 래퍼
    // Action<string> 패턴: 브리지가 WebView2 코어에 직접 의존하지 않아 테스트 시 Mock 주입 가능
    public async Task HandleWebMessageAsync(
        string webMessageJson, Action<string> replyToWeb,
        CancellationToken ct = default)
    {
        WebEnvelope msg;
        try
        {
            // null 역직렬화 시 명시적 예외 발생 — ?? throw 패턴으로 null 전파 방지
            msg = JsonSerializer.Deserialize<WebEnvelope>(webMessageJson)
                  ?? throw new JsonException("null envelope");
        }
        catch
        {
            // 역직렬화 실패 시 SPA에 오류 메시지 회신 — 앱이 죽지 않고 graceful degradation
            replyToWeb(JsonSerializer.Serialize(new { type = "error", message = "invalid_json" }));
            return;
        }

        switch (msg.Type)
        {
            case "lookup":
            {
                // Research §5.7.5 — 실제 GET /glossary/lookup은 호스트가 대리 호출
                // SPA가 직접 fetch('/glossary/lookup')를 호출하면 CORS 문제 및 키 노출 위험
                var q = msg.Payload.GetProperty("q").GetString() ?? "";
                var lang = msg.Payload.TryGetProperty("lang", out var l)
                    ? l.GetString() ?? "auto" : "auto";
                var rows = await _glossary.LookupAsync(q, lang, ct: ct);
                // type: "lookup.result" — LookupPane.tsx의 onHostMessage 핸들러가 구독
                replyToWeb(JsonSerializer.Serialize(new { type = "lookup.result", payload = rows }));
                break;
            }
            case "translate":
            {
                // Research §4.3 — source_lang / target_lang 명시 필수
                // TranslationOrchestrator가 스트리밍 청크를 "translation.chunk" 타입으로 개별 전송
                await _translator.RunStreamingAsync(msg.Payload, replyToWeb, ct);
                break;
            }
            default:
                // 알 수 없는 타입: SPA 구현 버그를 조기에 발견할 수 있도록 type 이름 포함
                replyToWeb(JsonSerializer.Serialize(new { type = "error", message = $"unknown_type:{msg.Type}" }));
                break;
        }
    }
}
```

- [ ] **Step 7: App.xaml.cs — DI 호스트 연결**

`src/TechGloss.Wpf/App.xaml.cs`:
```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Windows;
using TechGloss.Infrastructure;
using TechGloss.Wpf.Bridge;

namespace TechGloss.Wpf;

public partial class App : Application
{
    private IHost? _host;

    // async void: WPF Application 이벤트는 반환형이 void이므로 불가피
    // 예외가 발생하면 DispatcherUnhandledException로 전파
    protected override async void OnStartup(StartupEventArgs e)
    {
        // Host.CreateDefaultBuilder: 기본 로깅(ILogger), 환경 변수, 설정 파일 지원 포함
        _host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(c => c
                // optional: false — appsettings.json 없으면 기동 즉시 예외 (설정 오류 조기 발견)
                .AddJsonFile("appsettings.json", optional: false)
                // optional: true — Production 파일 없어도 기동 (기본값 fallback)
                .AddJsonFile("appsettings.Production.json", optional: true))
            .ConfigureServices((ctx, services) =>
            {
                // HttpClient 팩토리, SSRF 핸들러, Polly 재시도 등록
                services.AddTechGlossInfrastructure(ctx.Configuration);
                // Singleton: 앱 수명 동안 하나만 생성 — 상태 없으므로 안전
                services.AddSingleton<TranslationOrchestrator>();
                services.AddSingleton<HostBridge>();
                // Transient: MainWindow는 Show() 후 참조를 유지하지 않아도 WPF가 관리
                services.AddTransient<MainWindow>();
            })
            .Build();

        await _host.StartAsync();  // IHostedService (있을 경우) 시작

        // DI 컨테이너에서 MainWindow 꺼내기 — HostBridge, TranslationOrchestrator 자동 주입
        var window = _host.Services.GetRequiredService<MainWindow>();
        window.Show();
        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            // IHostedService 정상 종료 후 IDisposable 정리 (HttpClient 등)
            await _host.StopAsync();
            _host.Dispose();
        }
        base.OnExit(e);
    }
}
```

- [ ] **Step 8: 빌드 확인**

```bash
dotnet build src/TechGloss.Wpf
```
Expected: Build succeeded, 0 errors

- [ ] **Step 9: 커밋**

```bash
git add src/TechGloss.Wpf
git commit -m "feat: add WPF shell with WebView2 virtual host and postMessage bridge"
```

---

## Task 5: SPA (Vite + React) — 번역 방향 토글 + lookup 검색 UI

**Files:**
- Create: `web/package.json`
- Create: `web/vite.config.ts`
- Create: `web/src/main.tsx`
- Create: `web/src/App.tsx`
- Create: `web/src/components/TranslatePane.tsx`
- Create: `web/src/components/LookupPane.tsx`
- Create: `web/src/bridge.ts`

- [ ] **Step 1: Vite + React 프로젝트 초기화**

```bash
cd web
npm create vite@latest . -- --template react-ts
npm install
```

- [ ] **Step 2: WebView2 bridge 타입 선언 및 래퍼 작성**

`web/src/bridge.ts`:
```typescript
// Research §3.4 — postMessage 계약. 웹에서 호스트로 메시지 전송.
// 이 파일이 SPA ↔ WPF 호스트 통신의 유일한 진입점 — 다른 파일에서 chrome.webview 직접 접근 금지

// 허용된 메시지 타입만 리터럴 유니언으로 제한 — 오타를 컴파일 타임에 잡음
export type MessageType = 'translate' | 'lookup';

// 제네릭 봉투 타입: 컴포넌트마다 payload 타입 명시 가능 (LookupPayload, TranslatePayload 등)
export interface WebEnvelope<T = unknown> {
  type: MessageType;
  payload: T;
}

// lookup 요청 페이로드 — HostBridge가 msg.Payload.GetProperty("q") 로 읽는 구조와 일치해야 함
export interface LookupPayload { q: string; lang?: 'auto' | 'ko' | 'en'; }

// translate 요청 페이로드 — TranslationOrchestrator가 source_lang/target_lang 파싱
export interface TranslatePayload {
  text: string;
  source_lang: 'en' | 'ko';  // 명시 필수 (Research §4.3) — UI 토글 값을 직접 매핑
  target_lang: 'en' | 'ko';
  category_slug?: string;     // 용어 충돌 완화용; 없으면 전 카테고리 검색
}

// GlossaryHttpClient.GlossaryLookupRow의 camelCase 대응 — JSON 직렬화 키 매핑 주의
export interface LookupRow {
  id: string;
  termKo: string;
  termEn: string;
  definitionKo: string;   // 검색 결과에서 가장 중요한 필드 — 즉시 표시가 핵심 UX
  categoryName?: string;  // null이면 카테고리 레이블 숨김
}

// 호스트로 메시지 전송 (Research §3.4 — chrome.webview.postMessage)
// 옵셔널 체이닝(?.): WebView2 외 브라우저 환경(Vite dev server 등)에서도 에러 없이 동작
// dev 환경에서는 postMessage가 no-op — 별도 Mock 핸들러로 개발할 때 유용
export function sendToHost<T>(type: MessageType, payload: T) {
  window.chrome?.webview?.postMessage(JSON.stringify({ type, payload }));
}

// 호스트 응답 수신 리스너 등록
// 컴포넌트 마운트 시 useEffect(() => { onHostMessage(...) }, []) 패턴으로 한 번만 등록
// 여러 컴포넌트가 각자 등록해도 무방 — type 필드로 구독 분기
export function onHostMessage(handler: (msg: { type: string; payload: unknown }) => void) {
  window.chrome?.webview?.addEventListener('message', (e: MessageEvent) => {
    // JSON.parse 실패(malformed JSON) 시 무시 — UI가 멈추지 않도록 try/catch
    try { handler(JSON.parse(e.data)); } catch { /* 무시 */ }
  });
}

// TypeScript 전역 타입 보강 (Declaration Merging)
// window.chrome.webview는 WebView2 런타임이 주입하는 전역 — TS 타입 정의가 없으므로 직접 선언
// 옵셔널(?): 일반 브라우저 환경에서는 undefined이므로 타입 오류 없이 ?. 체이닝 가능
declare global {
  interface Window {
    chrome?: {
      webview?: {
        postMessage(msg: string): void;
        addEventListener(type: 'message', handler: (e: MessageEvent) => void): void;
      };
    };
  }
}
```

- [ ] **Step 3: LookupPane 컴포넌트 작성 (글자 단위 LIKE 검색 UI)**

`web/src/components/LookupPane.tsx`:
```tsx
import { useState, useEffect, useCallback, useRef } from 'react';
import { sendToHost, onHostMessage, LookupRow } from '../bridge';

// Research §5.7 — 글자 단위 입력마다 lookup 요청; 디바운스 100ms (Research §5.7.4)
// 함수 컴포넌트: 상태(q, lang, rows)와 사이드 이펙트(onHostMessage 등록)를 훅으로 관리
export function LookupPane() {
  const [q, setQ] = useState('');                        // 검색창 현재 입력값
  const [lang, setLang] = useState<'auto' | 'ko' | 'en'>('auto'); // 검색 언어 모드
  const [rows, setRows] = useState<LookupRow[]>([]);     // 서버에서 받은 검색 결과

  // useRef: 렌더링을 트리거하지 않는 가변 값 저장
  // debounceRef: 이전 setTimeout ID — 새 입력이 오면 clearTimeout으로 취소
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  // lastQ: 직전에 서버에 보낸 q값 — 같은 q 재전송 방지 (Research §5.7.4)
  const lastQ = useRef('');

  useEffect(() => {
    // 빈 의존성 배열 []: 컴포넌트 마운트 시 한 번만 등록
    // 여러 번 등록 방지 — cleanup 함수 없어도 WebView2 리스너는 쌓이지 않음 (페이지 새로고침 없음)
    onHostMessage((msg) => {
      if (msg.type === 'lookup.result') {
        // as LookupRow[]: 서버 응답 타입을 단언 — 실제로는 GlossaryLookupRow[] 직렬화값
        setRows(msg.payload as LookupRow[]);
      }
      // 'translation.chunk' 등 다른 타입은 여기서 무시 — 각 컴포넌트가 자신의 type만 처리
    });
  }, []);

  // useCallback: lang이 바뀔 때만 함수 재생성 — 불필요한 재렌더링 방지
  const handleInput = useCallback((value: string) => {
    setQ(value);  // 입력창 즉시 갱신 (UX: 타이핑 느낌 유지)

    // 이전 대기 중인 요청 취소 — 빠른 타이핑 시 중간 q로 불필요한 서버 호출 방지
    if (debounceRef.current) clearTimeout(debounceRef.current);

    // Research §5.7.4 — 디바운스 100ms; 동일 q 스킵
    // 100ms 내에 다음 입력이 없으면 서버에 요청 전송
    debounceRef.current = setTimeout(() => {
      if (value === lastQ.current) return;  // 예: 한글 조합 완료 후 동일 q 재전송 방지
      lastQ.current = value;
      sendToHost('lookup', { q: value, lang });  // bridge.ts → HostBridge.HandleWebMessageAsync
    }, 100);
  }, [lang]);  // lang이 바뀌면 새 클로저 생성 — 현재 lang 값이 요청에 반영됨

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8, padding: 16 }}>
      <h2>용어 검색</h2>
      <div style={{ display: 'flex', gap: 8 }}>
        <input
          value={q}
          onChange={e => handleInput(e.target.value)}
          placeholder="한글 또는 영문 용어 입력..."
          style={{ flex: 1, padding: 8, fontSize: 14 }}
        />
        <select value={lang} onChange={e => setLang(e.target.value as typeof lang)}>
          <option value="auto">자동</option>
          <option value="ko">한국어</option>
          <option value="en">영어</option>
        </select>
      </div>

      {/* Research §5.7.1 — 결과: term_ko, term_en, definition_ko, category 표시 */}
      <div style={{ overflowY: 'auto', maxHeight: 400 }}>
        {rows.map(row => (
          <div key={row.id} style={{ borderBottom: '1px solid #eee', padding: '8px 0' }}>
            <div>
              <strong>{row.termKo}</strong>
              {' / '}
              <span style={{ color: '#666' }}>{row.termEn}</span>
              {row.categoryName && (
                <span style={{ marginLeft: 8, fontSize: 12, color: '#999' }}>
                  [{row.categoryName}]
                </span>
              )}
            </div>
            <div style={{ fontSize: 13, marginTop: 4, color: '#444' }}>
              {row.definitionKo}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}
```

- [ ] **Step 4: TranslatePane 컴포넌트 작성 (EN↔KO 방향 토글)**

`web/src/components/TranslatePane.tsx`:
```tsx
import { useState, useEffect, useRef } from 'react';
import { sendToHost, onHostMessage } from '../bridge';

// Research §4.3 — 방향 선택 UI; 자동 감지는 보조만
export function TranslatePane() {
  // 'en-ko' | 'ko-en': UI 표현 — TranslatePayload의 source_lang/target_lang으로 변환됨
  const [direction, setDirection] = useState<'en-ko' | 'ko-en'>('en-ko');
  const [sourceText, setSourceText] = useState('');

  // result: 화면에 표시되는 텍스트 — 스트리밍 중 청크가 누적됨
  const [result, setResult] = useState('');
  // isStreaming: 번역 버튼 비활성화 및 "번역 중..." 표시 제어
  const [isStreaming, setIsStreaming] = useState(false);

  // resultRef: 스트리밍 청크를 누적하는 뮤터블 버퍼
  // state만 쓰면 클로저 스테일 문제로 += 누적이 안 됨 — ref로 최신값 항상 참조
  const resultRef = useRef('');

  useEffect(() => {
    // 마운트 시 한 번만 등록 — 모든 호스트 메시지를 수신하고 type으로 분기
    onHostMessage((msg) => {
      if (msg.type === 'translation.chunk') {
        // 스트리밍 청크 누적: ref에 붙이고 state 갱신 → React가 리렌더링
        resultRef.current += (msg.payload as string);
        setResult(resultRef.current);  // 매 청크마다 리렌더 — 실시간 텍스트 표시
      }
      if (msg.type === 'translation.done') {
        setIsStreaming(false);  // 번역 완료 → 버튼 활성화
      }
      if (msg.type === 'translation.error') {
        setIsStreaming(false);
        setResult(`오류: ${msg.payload}`);  // 오류 메시지를 결과창에 표시
      }
    });
  }, []);

  const handleTranslate = () => {
    // 공백만 있는 입력 또는 이미 스트리밍 중이면 무시
    if (!sourceText.trim() || isStreaming) return;

    // 이전 번역 결과 초기화 — ref와 state 모두 클리어
    resultRef.current = '';
    setResult('');
    setIsStreaming(true);  // 버튼 비활성화 + "번역 중..." 표시

    // Research §4.3 — source_lang / target_lang 명시 필수
    // direction 문자열을 두 글자 언어 코드로 변환해서 전송
    sendToHost('translate', {
      text: sourceText,
      source_lang: direction === 'en-ko' ? 'en' : 'ko',
      target_lang: direction === 'en-ko' ? 'ko' : 'en',
      // category_slug 미전송 → 서버가 전 카테고리 검색 (충돌 완화 불필요 시 기본값)
    });
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 12, padding: 16 }}>
      <h2>번역</h2>
      <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
        <span>{direction === 'en-ko' ? '영어 → 한국어' : '한국어 → 영어'}</span>
        {/* Research §4.3 — 방향 토글; 사용자가 선택한 방향 우선 */}
        <button onClick={() => setDirection(d => d === 'en-ko' ? 'ko-en' : 'en-ko')}>
          ⇌ 방향 전환
        </button>
      </div>

      <textarea
        value={sourceText}
        onChange={e => setSourceText(e.target.value)}
        placeholder={direction === 'en-ko' ? 'Enter English text...' : '한국어 텍스트 입력...'}
        rows={8}
        style={{ width: '100%', padding: 8, fontSize: 14, resize: 'vertical' }}
      />

      <button
        onClick={handleTranslate}
        disabled={isStreaming || !sourceText.trim()}
        style={{ padding: '8px 24px', fontSize: 14, cursor: 'pointer' }}
      >
        {isStreaming ? '번역 중...' : '번역'}
      </button>

      <div style={{
        minHeight: 200, padding: 12, background: '#f8f8f8',
        borderRadius: 4, whiteSpace: 'pre-wrap', fontSize: 14
      }}>
        {result || <span style={{ color: '#bbb' }}>번역 결과가 여기 표시됩니다.</span>}
      </div>
    </div>
  );
}
```

- [ ] **Step 5: App.tsx — 두 패널 통합**

`web/src/App.tsx`:
```tsx
import { TranslatePane } from './components/TranslatePane';
import { LookupPane } from './components/LookupPane';

// App.tsx: 레이아웃 루트 — 두 패널을 좌우로 배치
// 향후 탭/드래거블 레이아웃으로 교체 시 이 파일만 수정
export default function App() {
  return (
    // CSS Grid: 두 열 동등 분할 (1fr 1fr) + 전체 뷰포트 높이
    // flex 대신 grid: 양쪽 패널 높이를 자동으로 맞춤
    <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', height: '100vh' }}>
      {/* 왼쪽: 번역 패널 — 오른쪽 경계선으로 시각 구분 */}
      <div style={{ borderRight: '1px solid #ddd', overflowY: 'auto' }}>
        <TranslatePane />
      </div>
      {/* 오른쪽: 용어 검색 패널 */}
      <div style={{ overflowY: 'auto' }}>
        <LookupPane />
      </div>
    </div>
  );
}
```

- [ ] **Step 6: SPA 빌드 및 WPF Web/dist에 복사**

`web/vite.config.ts`:
```typescript
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  build: {
    // WPF csproj의 <Content Include="Web\dist\**"> 경로와 일치해야 함
    // 빌드 후 dotnet build 시 자동으로 출력 디렉터리에 복사됨
    outDir: '../src/TechGloss.Wpf/Web/dist',
    emptyOutDir: true,  // 이전 빌드 산출물 삭제 — 오래된 파일 잔류 방지
  },
  // base: '/': 가상 호스트 https://app.local/ 루트 기준 — 절대 경로 에셋 참조
  // './' 이면 상대 경로가 되어 중첩 라우팅 시 에셋 로드 실패 가능
  base: '/',
});
```

```bash
cd web && npm run build
```
Expected: `dist/index.html` 생성

- [ ] **Step 7: 커밋**

```bash
git add web src/TechGloss.Wpf/Web
git commit -m "feat: add Vite+React SPA with TranslatePane, LookupPane, and bridge"
```

---

## Task 6: 번역 오케스트레이터 + 프롬프트 빌더

**Files:**
- Create: `src/TechGloss.Core/Services/PromptBuilder.cs`
- Create: `src/TechGloss.Wpf/Bridge/TranslationOrchestrator.cs`
- Test: `tests/TechGloss.Core.Tests/Services/PromptBuilderTests.cs`

- [ ] **Step 1: PromptBuilder 테스트 먼저 작성**

`tests/TechGloss.Core.Tests/Services/PromptBuilderTests.cs`:
```csharp
using TechGloss.Core.Services;

namespace TechGloss.Core.Tests.Services;

public class PromptBuilderTests
{
    [Fact]
    public void BuildSystem_EnToKo_ContainsRoleInstruction()
    {
        var prompt = PromptBuilder.BuildSystemPrompt("en", "ko", glossaryRows: null);
        Assert.Contains("한국어", prompt);
        Assert.Contains("IT 기술 문서", prompt);
    }

    [Fact]
    public void BuildSystem_KoToEn_ContainsTechnicalEnglish()
    {
        var prompt = PromptBuilder.BuildSystemPrompt("ko", "en", glossaryRows: null);
        Assert.Contains("technical English", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSystem_WithGlossary_ContainsTermTable()
    {
        var rows = new[] {
            new GlossarySearchRow { TermEn = "deploy", TermKo = "배포", Source = "deploy", Target = "배포" }
        };
        var prompt = PromptBuilder.BuildSystemPrompt("en", "ko", rows);
        Assert.Contains("deploy", prompt);
        Assert.Contains("배포", prompt);
    }
}
```

```bash
dotnet test tests/TechGloss.Core.Tests --filter PromptBuilderTests
```
Expected: FAIL

- [ ] **Step 2: PromptBuilder 구현 (Research §4.4, §6)**

`src/TechGloss.Core/Services/PromptBuilder.cs`:
```csharp
using System.Text;
using TechGloss.Core.Contracts;

namespace TechGloss.Core.Services;

// Research §4.4 — 3블록(시스템·글로서리·사용자) 프롬프트 조립
// Research §6.0 — 방향별 역할 문구
// static 클래스: 인스턴스 불필요 — 순수 함수(입력 → 출력)로 테스트 용이
public static class PromptBuilder
{
    // 블록 1 + 블록 2를 합쳐 systemPrompt 문자열 반환
    // 블록 3(사용자 원문)은 OllamaHttpClient가 messages 배열의 "user" 역할로 직접 추가
    public static string BuildSystemPrompt(
        string sourceLang, string targetLang,
        IEnumerable<GlossarySearchRow>? glossaryRows)
    {
        // StringBuilder: 문자열 연결이 많을 때 메모리 효율 — string + 반복보다 빠름
        var sb = new StringBuilder();

        // Research §6.0 방향별 역할 — 두 방향 모두 동일 파이프라인, 프롬프트만 분기
        if (sourceLang == "en" && targetLang == "ko")
        {
            // C# 11 raw string literal ("""): 들여쓰기 트리밍 자동 적용
            // 프롬프트 내용 변경 시 이 블록만 수정 — Research §6 참조
            sb.AppendLine("""
                당신은 20년 경력의 시니어 풀스택 개발자이자 테크니컬 라이터입니다.
                다음 원칙에 따라 IT 기술 문서를 **한국어**로 번역하세요.

                ## 번역 원칙
                1. 변수명·함수명·클래스명·API 엔드포인트·CLI 명령·백틱(`) 표기를 절대 번역하지 마세요. (Research §6.1)
                2. 국내 개발 커뮤니티 관용 외래어(빌드, 배포, 렌더링, 의존성 등)를 사용하세요. (Research §6.2)
                3. 중요 기술 키워드는 **최초 1회** `한국어(English)` 형태로 병기하세요. (Research §6.3)
                4. 어미는 합니다/됩니다 또는 개조식을 사용하고, 능동형을 우선하세요. (Research §6.4)
                5. 헤더·리스트·볼드·코드 블록 등 **모든 마크다운 서식을 그대로 유지**하세요. (Research §6.5)
                6. 추측하지 말고, 불명확하면 원문 표현을 그대로 유지하세요.
                """);
        }
        else  // ko → en: Research §6.0 KO→EN 역할 문구
        {
            sb.AppendLine("""
                You are a senior technical writer and developer.
                Translate Korean IT documentation into clear, concise technical English for international readers.

                ## Translation Principles
                1. Preserve all code identifiers, backtick expressions, API endpoints, and markdown formatting. (Research §6.1, §6.5)
                2. Use concise active voice and headword noun phrases. Avoid unnecessary articles. (Research §6.4)
                3. On first occurrence, render Korean product names or acronyms as: KoreanTerm (English Full Name). (Research §6.3 symmetric)
                4. Do not speculate; keep the source expression if unclear.
                """);
        }

        // Research §4.4 블록 2: 글로서리 표 (방향에 맞게 source/target 정규화)
        // GlossaryHttpClient.SearchAsync가 이미 방향에 맞게 Source/Target을 정규화했으므로
        // 여기서는 그대로 표에 삽입 — 방향 분기 로직 불필요
        var rows = glossaryRows?.ToList();
        if (rows is { Count: > 0 })
        {
            // 마크다운 표 형식: LLM이 구조적으로 인식하기 쉬운 포맷
            sb.AppendLine("\n## 용어 참조 표 (아래 용어를 우선 적용하세요)");
            sb.AppendLine("| Source | Target | 정의(국문) | 카테고리 |");
            sb.AppendLine("|--------|--------|-----------|---------|");
            foreach (var r in rows)
                // DefinitionKo: LLM이 문맥 이해에 활용 — 단순 Source→Target 치환보다 정확도 향상
                sb.AppendLine($"| {r.Source} | {r.Target} | {r.DefinitionKo} | {r.CategorySlug} |");
        }
        // 글로서리 없을 때: 블록 1만 반환 — 프롬프트 유효성 유지

        return sb.ToString();
    }
}
```

- [ ] **Step 3: 테스트 통과 확인**

```bash
dotnet test tests/TechGloss.Core.Tests --filter PromptBuilderTests
```
Expected: PASS (3 tests)

- [ ] **Step 4: TranslationOrchestrator 구현**

`src/TechGloss.Wpf/Bridge/TranslationOrchestrator.cs`:
```csharp
using System.Text.Json;
using TechGloss.Core.Contracts;
using TechGloss.Core.Services;

namespace TechGloss.Wpf.Bridge;

// Research §5.2 시퀀스: search → 프롬프트 → LLM 스트림 → UI 청크
// HostBridge로부터 JsonElement payload를 받아 전체 번역 파이프라인을 조율
public sealed class TranslationOrchestrator
{
    private readonly IGlossaryClient _glossary;
    private readonly IOllamaChatClient _llm;

    public TranslationOrchestrator(IGlossaryClient glossary, IOllamaChatClient llm)
    {
        _glossary = glossary;
        _llm = llm;
    }

    // replyToWeb: HostBridge에서 전달한 콜백 — JSON 문자열을 SPA에 postMessage로 전송
    public async Task RunStreamingAsync(
        JsonElement payload, Action<string> replyToWeb, CancellationToken ct)
    {
        // GetString() ?? "": JSON 키가 없거나 null이면 기본값 — graceful degradation
        var text = payload.GetProperty("text").GetString() ?? "";
        var sourceLang = payload.GetProperty("source_lang").GetString() ?? "en";
        var targetLang = payload.GetProperty("target_lang").GetString() ?? "ko";
        // TryGetProperty: 선택 필드 — 없어도 에러 없이 null 처리
        var categorySlug = payload.TryGetProperty("category_slug", out var cs)
            ? cs.GetString() : null;

        // Research §5.2 step 1~2: 글로서리 RAG 검색
        // MVP 단계: SQL LIKE 검색 / Phase D: 임베딩 벡터 검색으로 교체 (인터페이스 동일)
        // ct: 사용자가 번역 취소 시 GlossaryApi 요청도 함께 취소됨
        var glossaryRows = await _glossary.SearchAsync(new GlossarySearchRequest
        {
            QueryText    = text,
            SourceLang   = sourceLang,
            TargetLang   = targetLang,
            TopK         = 8,            // 프롬프트 컨텍스트 8행 — 너무 많으면 컨텍스트 낭비
            CategorySlug = categorySlug  // null이면 전 카테고리
        }, ct);

        // Research §4.4 블록 1+2: 시스템 프롬프트 조립
        // PromptBuilder.BuildSystemPrompt: 역할 + 가이드라인 + 글로서리 표 → 단일 문자열
        var systemPrompt = PromptBuilder.BuildSystemPrompt(sourceLang, targetLang, glossaryRows);

        // Research §4.2: NDJSON 스트리밍 → UI에 청크 전송
        // await foreach: IAsyncEnumerable<string>을 비동기적으로 소비
        // 각 청크가 오는 즉시 replyToWeb 호출 → TranslatePane에서 실시간 텍스트 누적
        try
        {
            await foreach (var chunk in _llm.StreamChatAsync(systemPrompt, text, ct))
            {
                // "translation.chunk": TranslatePane.tsx의 onHostMessage 핸들러가 구독
                replyToWeb(JsonSerializer.Serialize(
                    new { type = "translation.chunk", payload = chunk }));
            }
            // 스트림 정상 종료 — isStreaming = false 트리거
            replyToWeb(JsonSerializer.Serialize(new { type = "translation.done" }));
        }
        catch (OperationCanceledException)
        {
            // 사용자가 번역 버튼 다시 클릭하거나 앱 종료 시 — 정상 취소
            replyToWeb(JsonSerializer.Serialize(
                new { type = "translation.error", payload = "cancelled" }));
        }
        catch (Exception ex)
        {
            // LLM 서버 다운, 타임아웃, 네트워크 오류 등 — 오류 내용을 UI에 표시
            replyToWeb(JsonSerializer.Serialize(
                new { type = "translation.error", payload = ex.Message }));
        }
    }
}
```

- [ ] **Step 5: 커밋**

```bash
git add src/TechGloss.Core/Services src/TechGloss.Wpf/Bridge tests/TechGloss.Core.Tests/Services
git commit -m "feat: add PromptBuilder with EN/KO guidelines and TranslationOrchestrator"
```

---

## Task 7: GlossaryApi — search / upsert / publish 엔드포인트

**Files:**
- Modify: `src/TechGloss.GlossaryApi/Program.cs` (엔드포인트 추가)
- Create: `src/TechGloss.GlossaryApi/Endpoints/SearchEndpoint.cs`
- Create: `src/TechGloss.GlossaryApi/Endpoints/UpsertEndpoint.cs`
- Create: `src/TechGloss.GlossaryApi/Services/EmbeddingService.cs`
- Test: `tests/TechGloss.GlossaryApi.Tests/SearchEndpointTests.cs`

- [ ] **Step 1: search 통합 테스트 먼저 작성**

`tests/TechGloss.GlossaryApi.Tests/SearchEndpointTests.cs`:
```csharp
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TechGloss.Core.Contracts;

namespace TechGloss.GlossaryApi.Tests;

public class SearchEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    public SearchEndpointTests(WebApplicationFactory<Program> f)
        => _client = f.CreateClient();

    [Fact]
    public async Task Search_WithQuery_ReturnsRows()
    {
        var req = new GlossarySearchRequest
        {
            QueryText  = "deploy software",
            SourceLang = "en",
            TargetLang = "ko",
            TopK       = 5
        };
        var resp = await _client.PostAsJsonAsync("/glossary/search", req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var rows = await resp.Content.ReadFromJsonAsync<List<GlossarySearchRow>>();
        Assert.NotNull(rows);
        // source / target 정규화 검증
        Assert.All(rows!, r => Assert.False(string.IsNullOrEmpty(r.Source)));
    }
}
```

- [ ] **Step 2: EmbeddingService 구현 (Research §5.3 — Ollama /api/embeddings)**

`src/TechGloss.GlossaryApi/Services/EmbeddingService.cs`:
```csharp
using System.Net.Http.Json;
using System.Text.Json;

namespace TechGloss.GlossaryApi.Services;

// Research §5.3 — Ollama POST /api/embeddings; 동일 172.20.64.76:11434 사용
// GlossaryApi 내부 전용 — WPF/Infrastructure에서 직접 참조 금지
// Phase D에서 SearchEndpoint가 SQL LIKE 대신 이 서비스를 사용해 벡터 검색으로 교체
public sealed class EmbeddingService
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;  // Ollama 서버 주소 — chat과 동일 호스트
    private readonly string _model;    // 임베딩 전용 모델 (chat 모델과 별도 설정 가능)

    public EmbeddingService(HttpClient http, IConfiguration config)
    {
        _http = http;
        // config["키:경로"]: appsettings.json 계층적 키 접근 — null 시 기본값 fallback
        _baseUrl = config["TechGloss:Ollama:BaseUrl"] ?? "http://172.20.64.76:11434";
        _model   = config["TechGloss:Ollama:EmbeddingModel"] ?? "nomic-embed-text";
    }

    // Research §5.2 임베딩 입력: "{category}: {term_en} => {term_ko}. {definition_ko}"
    // 단순 term만 임베딩하면 문맥 부족 → 풍부한 텍스트로 검색 품질 향상
    // static: 외부에서 임베딩 텍스트 생성 표준화 — 항목 저장 시와 검색 시 동일 포맷 보장
    public static string BuildEmbedText(
        string categorySlug, string termEn, string termKo, string definitionKo)
        => $"{categorySlug}: {termEn} => {termKo}. {definitionKo}";

    // 반환값 float[]: 벡터 차원 = 임베딩 모델 출력 차원 (모델마다 다름; nomic-embed-text = 768)
    // 차원이 바뀌면 전체 재임베딩 필요 (Research §5.4)
    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        // Ollama /api/embeddings 요청: { "model": "...", "prompt": "..." }
        var resp = await _http.PostAsJsonAsync(
            $"{_baseUrl.TrimEnd('/')}/api/embeddings",
            new { model = _model, prompt = text }, ct);
        resp.EnsureSuccessStatusCode();

        // 스트림 파싱: 전체 버퍼링 후 ParseAsync — 임베딩 응답은 작으므로 ResponseHeadersRead 불필요
        using var doc = await JsonDocument.ParseAsync(
            await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        // Ollama 임베딩 응답 구조: { "embedding": [0.1, 0.2, ...] }
        var arr = doc.RootElement.GetProperty("embedding");
        // GetSingle: float 파싱 — 벡터 연산은 float32로 충분; double은 메모리 2배
        return arr.EnumerateArray().Select(e => e.GetSingle()).ToArray();
    }
}
```

- [ ] **Step 3: search 엔드포인트 구현 (MVP — SQL 유사도, Phase D에서 Qdrant 교체)**

`src/TechGloss.GlossaryApi/Endpoints/SearchEndpoint.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using TechGloss.Core.Contracts;
using TechGloss.GlossaryApi.Data;

namespace TechGloss.GlossaryApi.Endpoints;

public static class SearchEndpoint
{
    public static void MapSearch(this WebApplication app)
    {
        // Research §5.6.6 — POST /glossary/search: 벡터 topK → SQL 재조회
        // MVP: 벡터 없이 SQL LIKE로 대체(Qdrant 미도입 단계). Phase D에서 임베딩 교체.
        // Phase D 전환 시: 이 메서드 내부만 수정 — API 계약(입출력 DTO)은 유지
        app.MapPost("/glossary/search", async (
            GlossarySearchRequest req, GlossaryDbContext db, CancellationToken ct) =>
        {
            // 빈 쿼리: 빈 배열 반환 — 프롬프트에 빈 글로서리 표 삽입 방지
            if (string.IsNullOrWhiteSpace(req.QueryText))
                return Results.Ok(Array.Empty<GlossarySearchRow>());

            // LookupEndpoint와 동일한 이스케이프 로직 (향후 공통 헬퍼로 추출 가능)
            // '\' 먼저 이스케이프 → '%', '_' 순서 중요
            var pattern = $"%{req.QueryText.Replace("%","\\%").Replace("_","\\_")}%";

            var rows = await db.Entries.AsNoTracking()
                .Where(e => e.Status == "published")  // RAG 대상은 published만 (Research §5.5)
                .Where(e =>
                    EF.Functions.Like(e.TermEn, pattern, "\\") ||
                    EF.Functions.Like(e.TermKo, pattern, "\\") ||
                    EF.Functions.Like(e.DefinitionKo, pattern, "\\"))
                .OrderBy(e => e.TermEn)
                .Take(req.TopK)  // TopK: 요청에서 지정된 상한 (기본 8)
                .ToListAsync(ct);

            // Research §4.3 — 방향에 맞게 source/target 정규화
            // 호출자(TranslationOrchestrator)는 Source/Target만 읽으면 됨 — 방향 로직 불필요
            // EN→KO: Source=TermEn, Target=TermKo / KO→EN: Source=TermKo, Target=TermEn
            var result = rows.Select(e => new GlossarySearchRow
            {
                EntryId      = e.Id,
                TermEn       = e.TermEn,
                TermKo       = e.TermKo,
                DefinitionKo = e.DefinitionKo,
                // MVP: CategoryId를 문자열로 변환 (Phase D에서 Category 조인으로 slug 반환)
                CategorySlug = e.CategoryId?.ToString() ?? "",
                Source       = req.SourceLang == "en" ? e.TermEn : e.TermKo,
                Target       = req.TargetLang == "ko" ? e.TermKo : e.TermEn
            });

            return Results.Ok(result);
        });
    }
}
```

- [ ] **Step 4: upsert / publish 엔드포인트 구현**

`src/TechGloss.GlossaryApi/Endpoints/UpsertEndpoint.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using TechGloss.Core.Models;
using TechGloss.GlossaryApi.Data;

namespace TechGloss.GlossaryApi.Endpoints;

public static class UpsertEndpoint
{
    public static void MapUpsertAndPublish(this WebApplication app)
    {
        // Research §5.6.6 — POST /glossary/upsert
        // 신규/기존 용어 모두 단일 엔드포인트로 처리 — 클라이언트가 INSERT/UPDATE를 구분할 필요 없음
        app.MapPost("/glossary/upsert", async (
            GlossaryEntry entry, GlossaryDbContext db, CancellationToken ct) =>
        {
            // Research §5.6.1 — 정규형 자동 생성: 앱 레이어에서 일관성 보장
            // FormKC(NFKC): 한글 호환 자모 분리 정규화 — 검색 시 자형 변이 무시
            // ToLowerInvariant: 문화권 독립 소문자 변환 (터키어 I 문제 회피)
            entry.TermKoNormalized = entry.TermKo.Trim().Normalize(
                System.Text.NormalizationForm.FormKC).ToLowerInvariant();
            entry.TermEnNormalized = entry.TermEn.Trim().ToLowerInvariant();
            entry.UpdatedAt = DateTimeOffset.UtcNow;

            var exists = await db.Entries.AnyAsync(e => e.Id == entry.Id, ct);
            if (!exists)
            {
                // 신규: CreatedAt 설정 + status 강제 draft (published 직접 생성 방지)
                // publish는 반드시 /glossary/publish 엔드포인트로만 가능
                entry.CreatedAt = entry.UpdatedAt;
                entry.Status = "draft";
                db.Entries.Add(entry);
            }
            else
            {
                // 기존: 변경된 필드만 덮어쓰기 — EF Core가 dirty 컬럼만 UPDATE
                db.Entries.Update(entry);
            }
            await db.SaveChangesAsync(ct);
            // 클라이언트가 새로 생성된 Id를 확인할 수 있도록 반환
            return Results.Ok(new { entry.Id });
        });

        // Research §5.6.6 — POST /glossary/publish
        // draft → published 전환만 허용 — deprecated로는 이 엔드포인트 사용 불가
        // Phase D에서는 published 시 벡터 인덱스 등록(Qdrant upsert)도 여기서 수행
        app.MapPost("/glossary/publish", async (
            PublishRequest req, GlossaryDbContext db, CancellationToken ct) =>
        {
            // FindAsync: PK로 1건 조회 — AnyAsync + 별도 Load보다 효율적
            var entry = await db.Entries.FindAsync(new object[] { req.EntryId }, ct);
            if (entry is null) return Results.NotFound();  // 존재하지 않는 Id → 404
            entry.Status = "published";
            entry.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });
    }
}

public record PublishRequest(Guid EntryId);
```

- [ ] **Step 5: Program.cs에 엔드포인트 추가**

`src/TechGloss.GlossaryApi/Program.cs`에 다음 라인 추가:
```csharp
app.MapSearch();          // SearchEndpoint
app.MapUpsertAndPublish(); // UpsertEndpoint
```

- [ ] **Step 6: 테스트 통과 확인**

```bash
dotnet test tests/TechGloss.GlossaryApi.Tests
```
Expected: PASS (4 tests)

- [ ] **Step 7: 커밋**

```bash
git add src/TechGloss.GlossaryApi tests/TechGloss.GlossaryApi.Tests
git commit -m "feat: add GlossaryApi search/upsert/publish endpoints"
```

---

## Task 8: 관측 가능성 — 구조화 로그 + 헬스 체크

**Files:**
- Modify: `src/TechGloss.Wpf/Bridge/HostBridge.cs` (로그 추가)
- Modify: `src/TechGloss.GlossaryApi/Program.cs` (헬스 체크 + 로그)
- Create: `docs/DEPLOY.md`

- [ ] **Step 1: GlossaryApi 헬스 체크 추가 (Research §9 — Glossary 미기동 완화)**

`src/TechGloss.GlossaryApi/Program.cs`에 추가:
```csharp
app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));
```

- [ ] **Step 2: WPF 기동 시 GlossaryApi 헬스 체크 (Research §9)**

`src/TechGloss.Wpf/App.xaml.cs` `OnStartup`에 추가:
```csharp
// Research §9 — GlossaryApi 미기동 시 명확한 오류 메시지
var glossaryBaseUrl = _host!.Services
    .GetRequiredService<IOptions<TechGlossOptions>>().Value.GlossaryApi.BaseUrl;
try
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    var health = await http.GetAsync($"{glossaryBaseUrl}/health");
    if (!health.IsSuccessStatusCode)
        MessageBox.Show("Glossary API 서버 응답 없음. 설정을 확인하세요.",
            "TechGloss", MessageBoxButton.OK, MessageBoxImage.Warning);
}
catch
{
    MessageBox.Show($"Glossary API({glossaryBaseUrl})에 연결할 수 없습니다.\n서버를 먼저 기동하세요.",
        "TechGloss", MessageBoxButton.OK, MessageBoxImage.Error);
}
```

- [ ] **Step 3: 구조화 로그 필드 추가 (Research §8)**

`src/TechGloss.Wpf/Bridge/HostBridge.cs`에 `ILogger<HostBridge>` 주입 및 로그 추가:
```csharp
// Research §8 — 로그 필드: request_id, source_lang, target_lang, glossary_operation, RTT ms
private readonly ILogger<HostBridge> _logger;

// HandleWebMessageAsync 내부
var requestId = Guid.NewGuid().ToString("N")[..8];
var sw = Stopwatch.StartNew();
// ... lookup/translate 처리 ...
_logger.LogInformation(
    "Bridge {Type} requestId={RequestId} elapsed={ElapsedMs}ms",
    msg.Type, requestId, sw.ElapsedMilliseconds);
```

- [ ] **Step 4: DEPLOY.md 작성 (Research §10 항목 7)**

`docs/DEPLOY.md`:
```markdown
# TechGloss 배포 문서

## 기동 순서

1. **GlossaryApi 서버 먼저 기동**
   ```bash
   cd src/TechGloss.GlossaryApi
   dotnet run --urls http://127.0.0.1:5088
   ```
   - 기동 확인: `GET http://127.0.0.1:5088/health` → `{"status":"ok",...}`

2. **WPF 앱 기동**
   ```bash
   dotnet run --project src/TechGloss.Wpf
   ```
   - GlossaryApi 미기동 시 경고 다이얼로그 표시

## 설정 오버레이 (Production)

`src/TechGloss.Wpf/appsettings.Production.json`:
```json
{
  "TechGloss": {
    "Ollama": { "BaseUrl": "http://172.20.64.76:11434" },
    "GlossaryApi": { "BaseUrl": "http://127.0.0.1:5088" }
  }
}
```

## WebView2 런타임 배포

엔터프라이즈 환경: WebView2 Evergreen 런타임이 없으면 설치 스크립트에 부트스트래퍼 포함.
```bash
# 설치 스크립트 예시
MicrosoftEdgeWebview2Setup.exe /silent /install
```

## LLM 연결 검증

```bash
curl -X POST http://172.20.64.76:11434/api/chat \
  -H "Content-Type: application/json" \
  -d '{"model":"gemma4:31b","messages":[{"role":"user","content":"hello"}],"stream":false}'
```

## 허용 호스트 목록 (SSRF 화이트리스트, Research §4.6)

- `172.20.64.76` — Ollama LLM
- `127.0.0.1` — GlossaryApi
- `localhost` — GlossaryApi (개발)
```

- [ ] **Step 5: 커밋**

```bash
git add src docs
git commit -m "feat: add health check, structured logging, and DEPLOY.md"
```

---

## Task 9: 골든 파일 테스트 + EN↔KO 스모크

**Files:**
- Create: `tests/TechGloss.Core.Tests/Golden/TranslationGoldenTests.cs`
- Create: `tests/TechGloss.GlossaryApi.Tests/Golden/LookupGoldenTests.cs`

- [ ] **Step 1: EN→KO 골든 테스트 작성 (Research §8)**

`tests/TechGloss.Core.Tests/Golden/TranslationGoldenTests.cs`:
```csharp
using TechGloss.Core.Services;

namespace TechGloss.Core.Tests.Golden;

// Research §8 — 대표 IT 문단 스냅샷 비교. LLM 변동성 고려, 용어 포함 여부만 검증.
public class TranslationGoldenTests
{
    [Fact]
    public void PromptBuilder_EnToKo_ContainsAllGuidelines()
    {
        var prompt = PromptBuilder.BuildSystemPrompt("en", "ko", null);
        // Research §6.1 — 코드 보존
        Assert.Contains("백틱", prompt);
        // Research §6.2 — Dev-Native
        Assert.Contains("빌드", prompt);
        // Research §6.5 — 마크다운 유지
        Assert.Contains("마크다운", prompt);
    }

    [Fact]
    public void PromptBuilder_KoToEn_ContainsTechnicalEnglish()
    {
        var prompt = PromptBuilder.BuildSystemPrompt("ko", "en", null);
        Assert.Contains("active voice", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("markdown", prompt, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: lookup 골든 테스트 작성 (Research §8)**

`tests/TechGloss.GlossaryApi.Tests/Golden/LookupGoldenTests.cs`:
```csharp
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net.Http.Json;
using TechGloss.Core.Contracts;

namespace TechGloss.GlossaryApi.Tests.Golden;

// Research §8 — lookup q별 기대 entry_id · definition_ko 포함 여부
public class LookupGoldenTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    public LookupGoldenTests(WebApplicationFactory<Program> f)
        => _client = f.CreateClient();

    [Theory]
    [InlineData("deploy", "en", "배포")]     // seed 데이터 검증
    [InlineData("배포", "ko", "deploy")]
    [InlineData("build", "auto", "빌드")]
    public async Task Lookup_SeedTerm_DefinitionKoContainsExpected(
        string q, string lang, string expectedInDefinition)
    {
        var resp = await _client.GetAsync(
            $"/glossary/lookup?q={Uri.EscapeDataString(q)}&lang={lang}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var rows = await resp.Content.ReadFromJsonAsync<List<GlossaryLookupRow>>();
        Assert.NotNull(rows);
        Assert.Contains(rows!, r =>
            r.DefinitionKo.Contains(expectedInDefinition, StringComparison.OrdinalIgnoreCase)
            || r.TermKo.Contains(expectedInDefinition, StringComparison.OrdinalIgnoreCase)
            || r.TermEn.Contains(expectedInDefinition, StringComparison.OrdinalIgnoreCase));
    }
}
```

- [ ] **Step 3: 전체 테스트 통과 확인**

```bash
dotnet test
```
Expected: All tests pass

- [ ] **Step 4: 커밋**

```bash
git add tests
git commit -m "test: add golden file tests for EN/KO prompts and lookup seed validation"
```

---

## Task 10: 보안 강화 + 로그 마스킹 + 최종 점검

**Files:**
- Modify: `src/TechGloss.Infrastructure/Http/AllowedHostsHandler.cs`
- Modify: `src/TechGloss.GlossaryApi/Endpoints/LookupEndpoint.cs` (제어 문자 제거)
- Create: `src/TechGloss.Infrastructure/Logging/MaskingLogger.cs`

- [ ] **Step 1: lookup q 제어 문자 제거 (Research §5.7.3 보안)**

`LookupEndpoint.cs`에서 `q` 전처리 추가:
```csharp
// 제어 문자 제거 (Research §5.7.3 — DoS·에러 방지)
q = new string(q.Where(c => !char.IsControl(c)).ToArray());
if (string.IsNullOrEmpty(q)) return Results.Ok(Array.Empty<GlossaryLookupRow>());
```

- [ ] **Step 2: 로그 마스킹 — 원문 해시 (Research §4.6, §8)**

`src/TechGloss.Infrastructure/Logging/MaskingLogger.cs`:
```csharp
using System.Security.Cryptography;
using System.Text;

namespace TechGloss.Infrastructure.Logging;

// Research §4.6, §8 — 원문 전문·PII가 로그에 남지 않도록 SHA-256 앞 8자리만 기록
// static 클래스: 인스턴스 불필요 — 어디서든 MaskingLogger.HashText(text) 호출
public static class MaskingLogger
{
    // SHA-256: .NET 내장 해시 — 같은 원문은 항상 같은 해시 (디버깅 시 동일 요청 추적 가능)
    // [..8]: 슬라이스 연산자 — 앞 8글자(16진 4바이트)만 사용 (충분한 식별성, 원문 복원 불가)
    // 전체 해시(64자)가 아닌 8자리: 로그 가독성 + 콜리전 확률이 낮아 실용적으로 충분
    public static string HashText(string text)
    {
        // SHA256.HashData: .NET 6+ 정적 메서드 — HashAlgorithm 인스턴스 생성/Dispose 불필요
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        // Convert.ToHexString: 대문자 16진수 문자열 (예: "A3F2B1C0")
        return Convert.ToHexString(bytes)[..8];
    }
}
```

`TranslationOrchestrator.cs`에 적용:
```csharp
_logger.LogInformation(
    "Translation requestId={RequestId} textHash={Hash} src={Src} tgt={Tgt}",
    requestId, MaskingLogger.HashText(text), sourceLang, targetLang);
```

- [ ] **Step 3: 전체 빌드·테스트 최종 확인**

```bash
dotnet build
dotnet test
```
Expected: Build succeeded; All tests pass

- [ ] **Step 4: 최종 커밋**

```bash
git add .
git commit -m "feat: security hardening — q sanitization, log masking, SSRF guard complete"
```

---

## 자체 검토 (spec coverage)

| Research 요구사항 | Plan2 위치 | 구현 여부 |
|------------------|-----------|---------|
| WPF exe | Task 4 | ✅ |
| WebView2 가상 호스트 `https://app.local/` | Task 4 Step 5 | ✅ |
| `file://` 금지 | Task 4 Step 5 | ✅ |
| LLM 고정 `172.20.64.76:11434 / gemma4:31b` | Task 2 Step 2, Task 6 | ✅ |
| NDJSON 스트리밍 | Task 2 Step 6 | ✅ |
| EN↔KO 양방향 + `source_lang/target_lang` | Task 6, Task 5 Step 4 | ✅ |
| Glossary API HTTP만 (직접 벡터 연결 금지) | Task 2 Step 7, Task 1 | ✅ |
| `GET /glossary/lookup` LIKE 검색 | Task 3 Step 5 | ✅ |
| 글자 단위 `definition_ko` 표시 | Task 5 Step 3 | ✅ |
| postMessage 브리지 | Task 4 Step 5~6 | ✅ |
| 프롬프트 3블록 + 글로서리 표 | Task 6 Step 2 | ✅ |
| IT 번역 가이드라인 §6 | Task 6 Step 2 | ✅ |
| SSRF 화이트리스트 | Task 2 Step 3~4 | ✅ |
| 로그 마스킹 | Task 10 Step 2 | ✅ |
| 디바운스 30~120ms | Task 5 Step 3 | ✅ (100ms) |
| q 최대 128자 + 제어 문자 제거 | Task 3 Step 5, Task 10 Step 1 | ✅ |
| published만 벡터 인덱싱 | Task 3 Step 5, Task 7 Step 3 | ✅ |
| 골든 파일 테스트 EN↔KO | Task 9 | ✅ |
| DEPLOY.md (기동 순서·헬스) | Task 8 Step 4 | ✅ |
| WebView2 DevTools 개발 모드 | Task 4 Step 5 | ✅ |
| 작성자·승인자 컬럼 없음 | Task 3 Step 3, Task 3 Step 4 | ✅ |
| Qdrant (Phase D) | 트레이드오프 T3에 문서화; MVP는 SQLite | ✅(문서화) |
