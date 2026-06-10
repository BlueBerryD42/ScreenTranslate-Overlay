using System.Windows.Input;

namespace GameTranslateOverlay.Helpers;

public readonly record struct ParsedHotkey(uint Modifiers, uint VirtualKey);

public static class HotkeyParser
{
    private static readonly Dictionary<string, uint> ModifierMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ctrl"] = 0x0002,
        ["Control"] = 0x0002,
        ["Alt"] = 0x0001,
        ["Shift"] = 0x0004,
        ["Win"] = 0x0008,
        ["Windows"] = 0x0008,
    };

    public static bool TryParse(string? hotkeyText, out ParsedHotkey hotkey, out string error)
    {
        hotkey = default;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(hotkeyText))
        {
            error = "Hotkey is empty.";
            return false;
        }

        var parts = hotkeyText.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            error = "Hotkey is empty.";
            return false;
        }

        uint modifiers = 0;
        var keyToken = parts[^1];

        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (!ModifierMap.TryGetValue(parts[i], out var mod))
            {
                error = $"Unknown modifier: {parts[i]}";
                return false;
            }

            modifiers |= mod;
        }

        if (!TryParseKey(keyToken, out var vk))
        {
            error = $"Unknown key: {keyToken}";
            return false;
        }

        hotkey = new ParsedHotkey(modifiers, vk);
        return true;
    }

    private static bool TryParseKey(string token, out uint vk)
    {
        vk = 0;
        if (token.Length >= 2 && token[0] == 'F' && int.TryParse(token.AsSpan(1), out var fn) && fn is >= 1 and <= 24)
        {
            vk = (uint)(0x70 + fn - 1);
            return true;
        }

        if (token.Length == 1)
        {
            var c = char.ToUpperInvariant(token[0]);
            if (c is >= 'A' and <= 'Z')
            {
                vk = (uint)c;
                return true;
            }

            if (c is >= '0' and <= '9')
            {
                vk = (uint)c;
                return true;
            }
        }

        if (Enum.TryParse<Key>(token, ignoreCase: true, out var wpfKey) && wpfKey != Key.None)
        {
            vk = (uint)KeyInterop.VirtualKeyFromKey(wpfKey);
            return vk != 0;
        }

        return false;
    }
}
