namespace TechGloss.Core.Models;

/// <summary>
/// ViewModel → TranslationOrchestrator 번역 요청 모델.
/// WebView2 postMessage 방식을 대체합니다.
/// </summary>
public sealed record TranslationRequest(
    string SourceText,
    string SourceLang,
    string TargetLang,
    string? CategoryName = null);
