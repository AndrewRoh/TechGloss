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
MicrosoftEdgeWebview2Setup.exe /silent /install
```

## LLM 연결 검증

```bash
curl -X POST http://172.20.64.76:11434/api/chat \
  -H "Content-Type: application/json" \
  -d '{"model":"gemma4:31b","messages":[{"role":"user","content":"hello"}],"stream":false}'
```

## 허용 호스트 목록 (SSRF 화이트리스트)

- `172.20.64.76` — Ollama LLM
- `127.0.0.1` — GlossaryApi
- `localhost` — GlossaryApi (개발)
