# TechGloss

WPF 데스크톱 앱 안에 WebView2(Chromium) 기반 SPA를 올리고, 사내 Ollama LLM과 로컬 Glossary API 서버를 연동해 EN↔KO 양방향 IT 기술 번역 및 용어 검색을 제공합니다.

## 주요 기능

- **양방향 번역** — EN→KO / KO→EN IT 기술 문서 번역 (스트리밍 출력)
- **용어 LIKE 검색** — 한 글자 입력마다 실시간 용어 사전 조회
- **RAG 의미 검색** — 임베딩 기반 유사 용어 검색 (Phase D)
- **용어 관리** — 신규 등록·수정·상태 전환(`draft` → `published` → `deprecated`)

## 아키텍처

```
Web UI (WebView2 SPA)         React + TypeScript (Vite)
    ↕ postMessage (JSON)
WPF Host (.NET 8)             App 진입점, HostBridge
    ├─ Translation Orchestrator   RAG + 스트리밍 번역
    ├─ LLM HTTP Client        → 172.20.64.76:11434  (gemma4:31b)
    └─ Glossary HTTP Client   → 127.0.0.1:5088

GlossaryApi (.NET 8)          ASP.NET Core Minimal API
    ├─ SQLite                 관계형 정본 (Dapper / EF Core)
    └─ Qdrant (Phase D)       벡터 RAG (Docker)
```

## 기술 스택

| 계층 | 기술 |
|------|------|
| WPF 쉘 | .NET 8 WPF + Microsoft.Web.WebView2 |
| SPA | Vite 8 + React 19 (TypeScript 6) |
| Glossary API | ASP.NET Core Minimal API |
| 관계형 DB | SQLite (EF Core) |
| 벡터 DB | Qdrant Docker (Phase D) |
| LLM | Ollama `/api/chat` NDJSON 스트리밍 |
| HTTP 공통 | System.Text.Json, Polly 재시도 |

## 프로젝트 구조

```
TechGloss.sln
Directory.Build.props              # net8.0, Nullable enable, ImplicitUsings
src/
  TechGloss.Core/                  # 도메인 모델·인터페이스 (외부 의존 없음)
  TechGloss.Infrastructure/        # HttpClient 팩토리·설정·SSRF 핸들러
  TechGloss.Wpf/                   # WPF 쉘·WebView2·HostBridge
  TechGloss.GlossaryApi/           # ASP.NET Core Minimal API + SQLite
web/                               # Vite+React SPA (빌드 → Wpf/Web/dist/)
tests/
  TechGloss.Core.Tests/
  TechGloss.GlossaryApi.Tests/
docs/DEPLOY.md
```

## 요구 사항

- .NET 8 SDK
- Node.js 20+
- WebView2 Evergreen 런타임 (또는 Fixed 런타임)
- Ollama 서버 (`172.20.64.76:11434`, 모델 `gemma4:31b` 로드 필요)
- Docker (Qdrant, Phase D 이후)

## 빌드

```bash
# 1. SPA 빌드 (web/ → src/TechGloss.Wpf/Web/dist/)
cd web && npm install && npm run build

# 2. 전체 솔루션 빌드
dotnet build TechGloss.sln
```

## 실행

서비스는 **GlossaryApi → WPF 앱** 순서로 기동합니다.

```bash
# GlossaryApi 먼저 기동
dotnet run --project src/TechGloss.GlossaryApi --urls http://127.0.0.1:5088

# 헬스 확인
curl http://127.0.0.1:5088/health
# → {"status":"ok","time":"..."}

# WPF 앱 기동
dotnet run --project src/TechGloss.Wpf
```

SPA 개발 서버만 단독으로 띄울 때:

```bash
cd web && npm run dev
```

## 테스트

```bash
# Core 단위 테스트
dotnet test tests/TechGloss.Core.Tests

# GlossaryApi 통합 테스트
dotnet test tests/TechGloss.GlossaryApi.Tests
```

## 설정

`src/TechGloss.Wpf/appsettings.json` 기본값:

```json
{
  "TechGloss": {
    "Ollama": {
      "BaseUrl": "http://172.20.64.76:11434",
      "Model": "gemma4:31b",
      "EmbeddingModel": "nomic-embed-text",
      "ChatPath": "/api/chat",
      "TimeoutSeconds": 120
    },
    "GlossaryApi": {
      "BaseUrl": "http://127.0.0.1:5088"
    }
  }
}
```

운영 환경 오버레이는 `appsettings.Production.json`에 작성합니다. 코드에 URL·모델명 직접 하드코딩 금지.

## 보안

- **SSRF 방지:** `AllowedHostsHandler`가 허용 호스트(`172.20.64.76`, `127.0.0.1`) 외 요청을 차단합니다.
- WebView2(SPA) 내부에서 Ollama·GlossaryApi로 직접 `fetch()` 호출 금지 — 반드시 WPF 호스트 `HttpClient` 경유.
- `TechGloss.Infrastructure`, `TechGloss.Wpf`에 `Qdrant.Client` 패키지 참조 금지.

## API 엔드포인트 (GlossaryApi)

| 메서드 | 경로 | 설명 |
|--------|------|------|
| `GET` | `/health` | 헬스 체크 |
| `GET` | `/glossary/lookup?q=...&lang=auto` | 문자열 LIKE 검색 |
| `POST` | `/glossary/search` | 의미 유사도 검색 (RAG) |
| `PUT` | `/glossary` | 용어 등록·수정 |
| `POST` | `/glossary/{id}/publish` | 상태 → `published` |

## 구현 로드맵

| Task | 내용 | 상태 |
|------|------|------|
| Task 1 | 솔루션 스캐폴딩 + Core 도메인 모델 | 완료 |
| Task 2 | Infrastructure — HttpClient 팩토리·SSRF 핸들러 | 완료 |
| Task 3 | GlossaryApi MVP — SQLite + LIKE 검색 | 완료 |
| Task 4 | WPF 쉘 + WebView2 + HostBridge | 완료 |
| Task 5 | SPA (Vite+React) + postMessage 연동 | 완료 |
| Task 6 | Translation Orchestrator — RAG + 스트리밍 번역 | 진행 중 |
| Task 7 | Qdrant 도입 + 임베딩 파이프라인 (Phase D) | 예정 |
| Task 8 | 배포 패키징 + 문서 | 예정 |

자세한 배포 절차는 [docs/DEPLOY.md](docs/DEPLOY.md)를 참조하세요.
