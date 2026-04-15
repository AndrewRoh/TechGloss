using System.Text;
using TechGloss.Core.Contracts;

namespace TechGloss.Core.Services;

public static class PromptBuilder
{
    public static string BuildSystemPrompt(
        string sourceLang, string targetLang,
        IEnumerable<GlossarySearchRow>? glossaryRows)
    {
        var sb = new StringBuilder();

        if (sourceLang == "en" && targetLang == "ko")
        {
            sb.AppendLine("""
                당신은 20년 경력의 시니어 풀스택 개발자이자 테크니컬 라이터입니다.
                다음 원칙에 따라 IT 기술 문서를 **한국어**로 번역하세요.

                ## 번역 원칙
                1. 변수명·함수명·클래스명·API 엔드포인트·CLI 명령·백틱(`) 표기를 절대 번역하지 마세요.
                2. 국내 개발 커뮤니티 관용 외래어(빌드, 배포, 렌더링, 의존성 등)를 사용하세요.
                3. 중요 기술 키워드는 **최초 1회** `한국어(English)` 형태로 병기하세요.
                4. 어미는 합니다/됩니다 또는 개조식을 사용하고, 능동형을 우선하세요.
                5. 헤더·리스트·볼드·코드 블록 등 **모든 마크다운 서식을 그대로 유지**하세요.
                6. 추측하지 말고, 불명확하면 원문 표현을 그대로 유지하세요.
                """);
        }
        else
        {
            sb.AppendLine("""
                You are a senior technical writer and developer.
                Translate Korean IT documentation into clear, concise technical English for international readers.

                ## Translation Principles
                1. Preserve all code identifiers, backtick expressions, API endpoints, and markdown formatting.
                2. Use concise active voice and headword noun phrases. Avoid unnecessary articles.
                3. On first occurrence, render Korean product names or acronyms as: KoreanTerm (English Full Name).
                4. Do not speculate; keep the source expression if unclear.
                """);
        }

        var rows = glossaryRows?.ToList();
        if (rows is { Count: > 0 })
        {
            sb.AppendLine("\n## 용어 참조 표 (아래 용어를 우선 적용하세요)");
            sb.AppendLine("| Source | Target | 정의(국문) | 카테고리 |");
            sb.AppendLine("|--------|--------|-----------|---------|");
            foreach (var r in rows)
                sb.AppendLine($"| {r.Source} | {r.Target} | {r.DefinitionKo} | {r.CategorySlug} |");
        }

        return sb.ToString();
    }
}
