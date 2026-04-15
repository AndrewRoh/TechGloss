using TechGloss.Core.Models;

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
