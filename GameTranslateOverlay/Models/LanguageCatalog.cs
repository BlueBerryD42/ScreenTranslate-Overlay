namespace GameTranslateOverlay.Models;

public static class LanguageCatalog
{
    public static IReadOnlyList<LanguageOption> SourceLanguages { get; } =
    [
        new("JA", "Japanese (JA)"),
        new("EN", "English (EN)"),
        new("DE", "German (DE)"),
        new("FR", "French (FR)"),
        new("ES", "Spanish (ES)"),
        new("ZH-HANS", "Chinese Simplified"),
        new("ZH-HANT", "Chinese Traditional"),
        new("KO", "Korean (KO)"),
    ];

    public static IReadOnlyList<LanguageOption> TargetLanguages { get; } =
    [
        new("EN-US", "English (US)"),
        new("EN-GB", "English (UK)"),
        new("DE", "German (DE)"),
        new("FR", "French (FR)"),
        new("ES", "Spanish (ES)"),
        new("ZH-HANS", "Chinese Simplified"),
        new("ZH-HANT", "Chinese Traditional"),
        new("VI", "Vietnamese (VI)"),
        new("KO", "Korean (KO)"),
    ];

    public static string? GetSourceLabel(string code)
    {
        foreach (var lang in SourceLanguages)
        {
            if (lang.Code.Equals(code, StringComparison.OrdinalIgnoreCase))
                return lang.Label;
        }

        return null;
    }

    public static string? GetTargetLabel(string code)
    {
        foreach (var lang in TargetLanguages)
        {
            if (lang.Code.Equals(code, StringComparison.OrdinalIgnoreCase))
                return lang.Label;
        }

        return null;
    }
}

public readonly record struct LanguageOption(string Code, string Label);
