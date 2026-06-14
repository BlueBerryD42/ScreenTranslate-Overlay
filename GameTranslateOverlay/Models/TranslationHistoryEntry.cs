namespace GameTranslateOverlay.Models;

public sealed record TranslationHistoryEntry(
    string SourceLang,
    string TargetLang,
    string SourceText,
    string NormalizedText,
    string Translation,
    DateTimeOffset TranslatedAt);
