using TechGloss.Core.Services;

namespace TechGloss.Core.Tests.Golden;

public class TranslationGoldenTests
{
    [Fact]
    public void PromptBuilder_EnToKo_ContainsAllGuidelines()
    {
        var prompt = PromptBuilder.BuildSystemPrompt("en", "ko", null);
        Assert.Contains("백틱", prompt);
        Assert.Contains("빌드", prompt);
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
