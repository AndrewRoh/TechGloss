namespace TechGloss.Core.Models;

public enum TranslationDirection
{
    EnToKo,
    KoToEn
}

public static class TranslationDirectionExtensions
{
    public static (string SourceLang, string TargetLang) ToLangPair(this TranslationDirection dir) =>
        dir == TranslationDirection.EnToKo ? ("en", "ko") : ("ko", "en");
}
