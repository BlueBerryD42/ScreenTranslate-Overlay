namespace GameTranslateOverlay.Core;

public static class DeepLLanguage
{
    public static string NormalizeForDeepL(string code, bool isTarget)
    {
        if (string.IsNullOrWhiteSpace(code))
            return isTarget ? "EN-US" : "JA";

        var normalized = code.Trim().ToUpperInvariant();

        if (normalized is "EN-GB")
            return "EN-GB";

        if (normalized is "EN" or "EN-US")
            return isTarget ? "EN-US" : "EN";

        return normalized switch
        {
            "JA" or "DE" or "FR" or "ES" or "VI" or "KO" => normalized,
            "ZH-HANS" or "ZH-HANT" or "ZH" => "ZH",
            _ => normalized,
        };
    }
}
