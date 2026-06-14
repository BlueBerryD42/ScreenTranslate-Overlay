using GameTranslateOverlay.Models;

namespace GameTranslateOverlay.Services;

public sealed class TranslationCache
{
    public const int MaxEntries = 100;
    public const int RecentDisplayCount = 10;

    private readonly LinkedList<TranslationHistoryEntry> _order = new();
    private readonly Dictionary<string, LinkedListNode<TranslationHistoryEntry>> _map = new();

    public bool TryGet(string sourceLang, string targetLang, string normalizedText, out TranslationHistoryEntry entry)
    {
        var key = MakeKey(sourceLang, targetLang, normalizedText);
        if (!_map.TryGetValue(key, out var node))
        {
            entry = null!;
            return false;
        }

        _order.Remove(node);
        _order.AddFirst(node);
        entry = node.Value;
        return true;
    }

    public void Add(
        string sourceLang,
        string targetLang,
        string sourceText,
        string normalizedText,
        string translation)
    {
        var key = MakeKey(sourceLang, targetLang, normalizedText);
        if (_map.TryGetValue(key, out var existing))
        {
            _order.Remove(existing);
            _map.Remove(key);
        }

        var historyEntry = new TranslationHistoryEntry(
            sourceLang,
            targetLang,
            sourceText,
            normalizedText,
            translation,
            DateTimeOffset.UtcNow);

        var node = _order.AddFirst(historyEntry);
        _map[key] = node;

        while (_order.Count > MaxEntries)
        {
            var oldest = _order.Last!.Value;
            _map.Remove(MakeKey(oldest.SourceLang, oldest.TargetLang, oldest.NormalizedText));
            _order.RemoveLast();
        }
    }

    public IReadOnlyList<TranslationHistoryEntry> GetRecent(int count = RecentDisplayCount)
    {
        var results = new List<TranslationHistoryEntry>(Math.Min(count, _order.Count));
        var node = _order.First;
        while (node is not null && results.Count < count)
        {
            results.Add(node.Value);
            node = node.Next;
        }

        return results;
    }

    public void Clear()
    {
        _map.Clear();
        _order.Clear();
    }

    private static string MakeKey(string sourceLang, string targetLang, string normalizedText) =>
        $"{sourceLang.ToUpperInvariant()}\u001f{targetLang.ToUpperInvariant()}\u001f{normalizedText}";
}
