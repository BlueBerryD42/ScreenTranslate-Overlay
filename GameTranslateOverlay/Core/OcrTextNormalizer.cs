using System.Text.RegularExpressions;

namespace GameTranslateOverlay.Core;

/// <summary>
/// Cleans raw Windows OCR output before translation.
/// </summary>
public static partial class OcrTextNormalizer
{
    public static string Normalize(string text, string sourceLang)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        normalized = MultiSpaceRegex().Replace(normalized, " ");
        normalized = MultiNewlineRegex().Replace(normalized, "\n");

        normalized = sourceLang.ToUpperInvariant() switch
        {
            "JA" or "ZH-HANS" or "ZH-HANT" or "KO" => RemoveCjkCharacterSpacing(normalized),
            "VI" => NormalizeVietnamese(normalized),
            _ => normalized,
        };

        return normalized.Trim();
    }

    private static string RemoveCjkCharacterSpacing(string text)
    {
        var result = text;
        for (var i = 0; i < 8; i++)
        {
            var next = CjkSpacedCharRegex().Replace(result, string.Empty);
            if (next == result)
                break;
            result = next;
        }

        return result;
    }

    private static string NormalizeVietnamese(string text)
    {
        var result = SpaceBeforePunctuationRegex().Replace(text, "$1");
        if (LooksCharacterSpaced(result))
            result = result.Replace(" ", string.Empty);

        return MultiSpaceRegex().Replace(result, " ").Trim();
    }

    private static bool LooksCharacterSpaced(string text)
    {
        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 4)
            return false;

        var singleCharCount = tokens.Count(token => token.Length == 1);
        return singleCharCount >= tokens.Length * 0.6;
    }

    [GeneratedRegex(@"[ \t]{2,}")]
    private static partial Regex MultiSpaceRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex MultiNewlineRegex();

    [GeneratedRegex(@"(?<=[\u3040-\u309F\u30A0-\u30FF\u4E00-\u9FFF\uFF00-\uFFEF\u3400-\u4DBF々ー…]) (?=[\u3040-\u309F\u30A0-\u30FF\u4E00-\u9FFF\uFF00-\uFFEF\u3400-\u4DBF々ー…])")]
    private static partial Regex CjkSpacedCharRegex();

    [GeneratedRegex(@"\s+([,.!?;:])")]
    private static partial Regex SpaceBeforePunctuationRegex();
}
