using TechGloss.Core.Contracts;
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
