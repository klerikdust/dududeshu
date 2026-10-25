using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;

namespace SamplePlugin.Services;

/// <summary>
/// Passively records short Japanese chat lines the plugin translated, so the
/// ones players keep typing can be reviewed and added to the user's glossary.
/// Only the line, how often it was seen, a hash of each sender and Google's
/// translation are kept, in <c>glossary_candidates.json</c> in the plugin
/// config folder. Nothing leaves the machine.
///
/// Long sentences are skipped: they never recur word for word, so they could
/// never match as a glossary entry. A line counts as recurring once several
/// different players have used it, which filters out one person's typos and
/// in-jokes.
/// </summary>
public sealed class GlossaryCollector : IDisposable
{
    public const string FileName = "glossary_candidates.json";

    public const int RecurringCount = 3;
    public const int RecurringSenders = 2;

    // Longer than this and the line is a sentence, not a set phrase or term.
    private const int MaxLength = 32;
    private const int MaxLines = 2000;
    private const int MaxSendersPerLine = 20;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMinutes(1);

    // What may trail a line without changing it: おつ！ and おつｗｗ are おつ.
    private static readonly char[] Trailing = "、,。.!~〜…♪☆★♡♥ w笑".ToCharArray();
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly object gate = new();
    private readonly object fileGate = new();
    private readonly string path;
    private readonly Func<FfxivGlossary> glossary;
    private readonly Timer flushTimer;
    private Dictionary<string, CollectedLine> lines = new(StringComparer.Ordinal);
    private HashSet<string> dismissed = new(StringComparer.Ordinal);
    private bool dirty;

    public GlossaryCollector(string configDirectory, Func<FfxivGlossary> glossary)
    {
        path = Path.Combine(configDirectory, FileName);
        this.glossary = glossary;
        Load();
        flushTimer = new Timer(_ => Flush(), null, FlushInterval, FlushInterval);
    }

    public void Dispose()
    {
        flushTimer.Dispose();
        Flush();
    }

    public int Count
    {
        get { lock (gate) return lines.Count; }
    }

    /// <summary>
    /// Records one translated Japanese chat line. Lines the glossary already
    /// covers, dismissed lines and sentences are ignored.
    /// </summary>
    public void Record(string japanese, string sender, string translation, string translationLang)
    {
        var display = Clean(japanese);
        if (display.Length is 0 or > MaxLength || !display.Any(IsJapaneseChar))
            return;

        var key = FfxivGlossary.KeyOf(display);
        if (glossary().Knows(display))
            return;

        lock (gate)
        {
            if (dismissed.Contains(key))
                return;

            var now = DateTime.UtcNow;
            if (!lines.TryGetValue(key, out var line))
            {
                if (lines.Count >= MaxLines)
                    Evict();
                lines[key] = line = new CollectedLine { Japanese = display, FirstSeen = now };
            }

            line.Count++;
            line.LastSeen = now;
            var senderHash = Hash(sender);
            if (senderHash != null && line.Senders.Count < MaxSendersPerLine && !line.Senders.Contains(senderHash))
                line.Senders.Add(senderHash);
            if (!string.IsNullOrWhiteSpace(translation))
            {
                line.Translation = translation;
                line.TranslationLang = translationLang;
            }

            dirty = true;
        }
    }

    /// <summary>Collected lines, those seen from the most players first.</summary>
    public IReadOnlyList<CollectedLine> Snapshot()
    {
        lock (gate)
        {
            return lines.Values
                .OrderByDescending(l => l.IsRecurring)
                .ThenByDescending(l => l.Senders.Count)
                .ThenByDescending(l => l.Count)
                .ThenByDescending(l => l.LastSeen)
                .Select(l => l.Clone())
                .ToList();
        }
    }

    /// <summary>Drops the line, e.g. once it has been added to the glossary.</summary>
    public void Remove(string japanese)
    {
        lock (gate)
        {
            if (lines.Remove(FfxivGlossary.KeyOf(japanese)))
                dirty = true;
        }
        Flush();
    }

    /// <summary>Drops the line and never collects it again.</summary>
    public void Dismiss(string japanese)
    {
        var key = FfxivGlossary.KeyOf(japanese);
        lock (gate)
        {
            lines.Remove(key);
            dismissed.Add(key);
            dirty = true;
        }
        Flush();
    }

    /// <summary>Forgets every collected line. Dismissed lines stay dismissed.</summary>
    public void Clear()
    {
        lock (gate)
        {
            lines.Clear();
            dirty = true;
        }
        Flush();
    }

    /// <summary>Drops lines the (reloaded) glossary now covers.</summary>
    public void Prune()
    {
        var current = glossary();
        lock (gate)
        {
            foreach (var key in lines.Where(p => current.Knows(p.Value.Japanese)).Select(p => p.Key).ToList())
            {
                lines.Remove(key);
                dirty = true;
            }
        }
        Flush();
    }

    public void Flush()
    {
        string json;
        lock (gate)
        {
            if (!dirty)
                return;
            json = JsonSerializer.Serialize(
                new CollectorFile { Lines = lines.Values.ToList(), Dismissed = dismissed.ToList() }, JsonOptions);
            dirty = false;
        }

        // The flush timer and the review window can both get here.
        lock (fileGate)
        {
            try
            {
                var temp = path + ".tmp";
                File.WriteAllText(temp, json, new UTF8Encoding(false));
                File.Move(temp, path, true);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning($"[dudu的書] could not save collected glossary lines: {ex.Message}");
            }
        }
    }

    // Seen once, longest ago, goes first; a line several players use is kept.
    private void Evict()
    {
        foreach (var key in lines
                     .OrderBy(p => p.Value.Senders.Count)
                     .ThenBy(p => p.Value.Count)
                     .ThenBy(p => p.Value.LastSeen)
                     .Take(MaxLines / 10)
                     .Select(p => p.Key)
                     .ToList())
            lines.Remove(key);
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(path))
                return;
            var file = JsonSerializer.Deserialize<CollectorFile>(File.ReadAllText(path), JsonOptions);
            if (file == null)
                return;

            foreach (var line in file.Lines ?? new())
            {
                if (!string.IsNullOrWhiteSpace(line.Japanese))
                    lines[FfxivGlossary.KeyOf(line.Japanese)] = line;
            }
            dismissed = new HashSet<string>(file.Dismissed ?? new(), StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"[dudu的書] collected glossary lines unreadable, starting fresh: {ex.Message}");
        }
    }

    private static string Clean(string text)
    {
        var s = Whitespace.Replace(text.Normalize(NormalizationForm.FormKC), " ").Trim();
        // おつw stays おつ, but a bare w or 笑 is kept as itself.
        var core = s.TrimEnd(Trailing);
        return core.Length > 0 ? core : s;
    }

    private static bool IsJapaneseChar(char c) => c is >= '぀' and <= 'ヿ' or >= '一' and <= '鿿' or '々';

    // Only enough to tell players apart; names are never stored.
    private static string? Hash(string sender)
    {
        if (string.IsNullOrWhiteSpace(sender))
            return null;
        var hash = 2166136261u;
        foreach (var c in sender.Trim().ToLowerInvariant())
        {
            hash ^= c;
            hash *= 16777619u;
        }
        return hash.ToString("x8");
    }

    private sealed class CollectorFile
    {
        [JsonPropertyName("lines")]
        public List<CollectedLine>? Lines { get; set; }

        [JsonPropertyName("dismissed")]
        public List<string>? Dismissed { get; set; }
    }
}

public sealed class CollectedLine
{
    [JsonPropertyName("ja")]
    public string Japanese { get; set; } = string.Empty;

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("senders")]
    public List<string> Senders { get; set; } = new();

    [JsonPropertyName("translation")]
    public string Translation { get; set; } = string.Empty;

    [JsonPropertyName("translationLang")]
    public string TranslationLang { get; set; } = string.Empty;

    [JsonPropertyName("firstSeen")]
    public DateTime FirstSeen { get; set; }

    [JsonPropertyName("lastSeen")]
    public DateTime LastSeen { get; set; }

    [JsonIgnore]
    public bool IsRecurring =>
        Count >= GlossaryCollector.RecurringCount && Senders.Count >= GlossaryCollector.RecurringSenders;

    public CollectedLine Clone()
    {
        var copy = (CollectedLine)MemberwiseClone();
        copy.Senders = new List<string>(Senders);
        return copy;
    }
}
