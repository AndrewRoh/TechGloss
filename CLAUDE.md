# TechGloss — CLAUDE.md

## 프로젝트 개요

WPF exe 안에 WebView2(Chromium) 기반 현대 웹 UI를 올리고, 사내 고정 Ollama LLM(`gemma4:31b`)과 로컬 Glossary API 서버를 연동해 EN↔KO 양방향 IT 기술 번역 및 글자 단위 용어 LIKE 검색을 제공하는 데스크톱 앱.

## 아키텍처

```
Web UI (WebView2 SPA)
    ↕ postMessage (JSON)
WPF Host (.NET 8)
    ├─ Translation Orchestrator
    ├─ LLM HTTP Client       → 172.20.64.76:11434 (gemma4:31b)
    └─ Glossary HTTP Client  → 127.0.0.1:5088 (GlossaryApi)
                                  ├─ SQLite (관계형 정본)
                                  └─ Qdrant (벡터 RAG, Phase D)
```

**핵심 원칙:**
- WPF/Infrastructure는 Qdrant에 **직접 연결 금지** — GlossaryApi 전용
- Ollama·GlossaryApi 호출은 반드시 WPF 호스트 `HttpClient` — WebView 내 `fetch()` 직접 호출 금지
- 모든 번역 요청에 `source_lang` / `target_lang` 명시 필수 (자동 감지는 UI 보조만)
- 의미 검색(`POST /glossary/search`) ↔ 문자열 검색(`GET /glossary/lookup`) 경로 혼합 금지

## 기술 스택

| 계층 | 기술 |
|------|------|
| WPF 쉘 | .NET 8 WPF + Microsoft.Web.WebView2 |
| SPA | Vite + React (TypeScript) |
| Glossary API | ASP.NET Core Minimal API |
| 관계형 DB | SQLite (Dapper / EF Core) |
| 벡터 DB | Qdrant (Docker, Phase D) |
| LLM | Ollama HTTP API (`/api/chat`, NDJSON) |
| HTTP 공통 | System.Text.Json, Polly (재시도) |

## 고정 엔드포인트

| 구분 | 값 |
|------|-----|
| Ollama LLM | `http://172.20.64.76:11434` |
| LLM 모델 | `gemma4:31b` |
| 임베딩 모델 | `nomic-embed-text` (appsettings `EmbeddingModel` 키) |
| GlossaryApi | `http://127.0.0.1:5088` |
| Ollama 경로 | `/api/chat` (NDJSON, `UseOpenAiCompatiblePath: false`) |

## 프로젝트 구조

```
TechGloss.sln
Directory.Build.props              # net8.0, Nullable enable, ImplicitUsings
src/
  TechGloss.Core/                  # 도메인 모델·인터페이스 (의존성 없음)
  TechGloss.Infrastructure/        # HttpClient 팩토리·설정·SSRF 핸들러
  TechGloss.Wpf/                   # WPF 쉘·WebView2·HostBridge
  TechGloss.GlossaryApi/           # ASP.NET Core Minimal API + DB
web/                               # Vite+React SPA (빌드 → Wpf/Web/dist/)
tests/
  TechGloss.Core.Tests/            # Core 단위 테스트
  TechGloss.GlossaryApi.Tests/     # GlossaryApi 통합 테스트
docs/DEPLOY.md
```

## 핵심 타입

### 도메인 (TechGloss.Core)

- `GlossaryEntry` — SQLite 테이블 매핑. `Id`(Guid) = Qdrant point id
- `GlossaryCategory` — 계층 카테고리, `Slug`(URL용) / `Name`(한글 표시)
- `TranslationDirection` — `EnToKo` / `KoToEn` 열거형. `.ToLangPair()` 확장
- `WebEnvelope` — postMessage 계약. `Type` 필드로 분기, `Payload`는 `JsonElement`
- `GlossarySearchRequest` — RAG 검색 요청 (TopK 기본 8, CategorySlug 선택)
- `GlossarySearchRow` — RAG 결과. `Source`/`Target`은 방향에 맞게 이미 정규화
- `GlossaryLookupRow` — LIKE 검색 결과 (UI 평탄 구조)

### 인터페이스 (TechGloss.Core.Contracts)

- `IGlossaryClient` — `SearchAsync`, `LookupAsync`, `UpsertAsync`, `PublishAsync`
- `IOllamaChatClient` — `StreamChatAsync` → `IAsyncEnumerable<string>` (토큰 델타)

### 설정 (TechGloss.Infrastructure.Options)

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

## 보안 규칙

- **SSRF 방지:** `AllowedHostsHandler` — 허용 호스트 외 요청 즉시 `InvalidOperationException`
- 허용 목록: `172.20.64.76`, `127.0.0.1` (기본값)
- WebView2 내부에서 외부 HTTP 직접 호출 절대 금지

## 검색 두 경로

| 경로 | 엔드포인트 | 용도 |
|------|------------|------|
| 의미 유사도 | `POST /glossary/search` | RAG — 임베딩 + 벡터 (Phase D에서 Qdrant, MVP는 SQL LIKE 내부 구현) |
| 글자 LIKE | `GET /glossary/lookup?q=...&lang=auto` | 한 글자씩 입력 실시간 조회 — SQL LIKE, 벡터 불필요 |

## GlossaryEntry 상태 흐름

`draft` → `published` (RAG 인덱스 활성) → `deprecated` (검색 제외)

## WebView2 ↔ 호스트 브리지

- **방식:** `postMessage + JSON` 우선 (COM AddHostObjectToScript 미사용)
- SPA: `chrome.webview.postMessage(JSON.stringify({ type, payload }))`
- 호스트: `WebMessageReceived` → `JsonSerializer.Deserialize<WebEnvelope>` → `Type`으로 switch 분기
- 스트리밍: 호스트 → SPA 방향은 `CoreWebView2.PostWebMessageAsJson`으로 토큰 델타 전달

## 구현 순서 (Plan.md 기준)

| Task | 내용 | 상태 |
|------|------|------|
| Task 1 | 솔루션 스캐폴딩 + Core 도메인 모델 | - [ ] |
| Task 2 | Infrastructure — HttpClient 팩토리·설정·SSRF 핸들러 | - [ ] |
| Task 3 | GlossaryApi MVP — SQLite + LIKE 검색 | - [ ] |
| Task 4 | WPF 쉘 + WebView2 + HostBridge | - [ ] |
| Task 5 | SPA (Vite+React) + postMessage 연동 | - [ ] |
| Task 6 | Translation Orchestrator — RAG + 스트리밍 번역 | - [ ] |
| Task 7 | Qdrant 도입 + 임베딩 파이프라인 (Phase D) | - [ ] |
| Task 8 | 배포 패키징 + 문서 | - [ ] |

## 개발 규칙

- `Directory.Build.props`에서 전역 `<TargetFramework>net8.0</TargetFramework>` 적용 — 개별 csproj 중복 금지
- `TechGloss.Infrastructure`와 `TechGloss.Wpf`에 `Qdrant.Client` 패키지 참조 금지
- `appsettings.Production.json` 환경 오버레이만 허용 — LLM URL·모델 코드 하드코딩 금지
- `TermKoNormalized` / `TermEnNormalized` 컬럼은 앱 레이어에서 NFKC + Trim + ToLower 적용 후 저장
- `GlossaryEntry.Id`(Guid)는 Qdrant point id와 동일 값 유지 — SQL ↔ 벡터 동기화 단순화

## 테스트

```bash
# Core 단위 테스트
dotnet test tests/TechGloss.Core.Tests

# SSRF 핸들러 테스트
dotnet test tests/TechGloss.Core.Tests --filter AllowedHostsHandlerTests

# GlossaryApi 통합 테스트
dotnet test tests/TechGloss.GlossaryApi.Tests
```

## 빌드

```bash
# SPA 빌드 (web/ → src/TechGloss.Wpf/Web/dist/)
cd web && npm run build

# 전체 솔루션 빌드
dotnet build TechGloss.sln

# WPF 단독 실행
dotnet run --project src/TechGloss.Wpf

# GlossaryApi 단독 실행
dotnet run --project src/TechGloss.GlossaryApi
```
