using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SamplePlugin.Services;

/// <summary>
/// FFXIV glossary applied to Japanese text before it reaches the translator.
/// Google translates JP raid callouts, Party Finder shorthand and chat slang
/// literally (消化 → "digestion", 頭割り → "head split", 野良 → "stray"), so
/// those terms are swapped for their English community equivalents first and
/// Google only has to translate the grammar around them.
///
/// Entries live in <c>Data/ffxiv_jp_glossary.json</c>, embedded at build time
/// and compiled from <c>Data/ffxiv_jp_en_dataset.json</c> plus curated
/// additions. They come in two kinds:
/// <list type="bullet">
/// <item><b>phrase</b>: a whole chat line (よろしくお願いします, おつ, 草). Used
/// only when every segment of a message is a known entry; the English is then
/// composed directly and Google never sees the Japanese.</item>
/// <item><b>term</b>: jargon injected inside a sentence, with script-boundary
/// checks so it never fires inside a longer word (ナイト in ナイトメア, 線 in
/// 直線).</item>
/// </list>
/// </summary>
public sealed class FfxivGlossary
{
    private const string ResourceName = "dududeshu.ffxiv_jp_glossary.json";

    // Where a message splits into independently glossed segments. '?' is
    // deliberately absent: dropping it would answer 大丈夫? with "It's all
    // right." instead of asking.
    private const string SegmentBreaks = "、,。.!~〜…♪☆★♡♥";

    // Kanji that attach to a one- or two-kanji term without forming a
    // different word (消化中, 固定組, 未消化). Any other kanji neighbour means
    // the term is part of another word: 固定位置, 範囲内, 直線.
    private const string KanjiSuffixes = "中済組後前時用式勢回目側";
    private const string KanjiPrefixes = "未初再今各全";

    // Stand-ins for glossary terms when translating into a language other
    // than English. Google carries X1, X2… through untouched and places them
    // sensibly ("請X1", "Setelah X1 adalah X2"); without them it re-translates
    // the injected English literally (farming → 養殖, wipe → 擦拭).
    private static readonly Regex Placeholder = new(
        @"(?<![A-Za-z])X(\d{1,3})(?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly Dictionary<string, Line> lines = new(StringComparer.Ordinal);
    private readonly Dictionary<char, Term[]> termsByFirstChar;

    public static FfxivGlossary Empty { get; } = new(Array.Empty<Entry>());

    public int Count { get; }

    private FfxivGlossary(IReadOnlyCollection<Entry> entries)
    {
        var terms = new Dictionary<char, List<Term>>();
        foreach (var entry in entries)
        {
            var key = Fold(entry.Japanese).TrimEnd(SegmentBreaks.ToCharArray());
            if (key.Length == 0)
                continue;

            // A phrase wins a tie with a term so the line keeps its sentence form.
            if (entry.IsPhrase || !lines.ContainsKey(key))
                lines[key] = new Line(entry.English, entry.IsPhrase);
            Count++;

            if (entry.IsPhrase)
                continue;

            if (!terms.TryGetValue(key[0], out var bucket))
                terms[key[0]] = bucket = new List<Term>();
            bucket.Add(new Term(key, entry.English, entry.Next, key.Length <= 2 && key.All(IsKanji)));
        }

        termsByFirstChar = terms.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.OrderByDescending(term => term.Key.Length).ToArray());
    }

    public static FfxivGlossary LoadEmbedded()
    {
        using var stream = typeof(FfxivGlossary).Assembly.GetManifestResourceStream(ResourceName)
                           ?? throw new FileNotFoundException($"Embedded resource '{ResourceName}' is missing.");
        return Parse(stream);
    }

    public static FfxivGlossary Parse(Stream json)
    {
        using var doc = JsonDocument.Parse(json);
        var entries = new List<Entry>();
        foreach (var row in doc.RootElement.GetProperty("entries").EnumerateArray())
        {
            var japanese = row.GetProperty("ja").GetString();
            var english = row.GetProperty("en").GetString();
            if (string.IsNullOrWhiteSpace(japanese) || string.IsNullOrWhiteSpace(english))
                continue;

            var isPhrase = row.TryGetProperty("mode", out var mode) && mode.GetString() == "phrase";
            var next = !row.TryGetProperty("next", out var rule)
                ? Follow.Any
                : rule.GetString() switch
                {
                    "katakana" => Follow.Katakana,
                    "end" => Follow.End,
                    _ => Follow.Any,
                };
            entries.Add(new Entry(japanese, english.Trim(), isPhrase, next));
        }

        return new FfxivGlossary(entries);
    }

    /// <summary>
    /// Translates Japanese <paramref name="text"/> with the glossary applied.
    /// Returns null when no entry matched, so the caller falls back to its
    /// plain translation path.
    /// </summary>
    public async Task<TranslationResult?> TranslateAsync(
        Translator translator, string text, string targetLang, CancellationToken ct)
    {
        var plan = Prepare(text);
        var toEnglish = string.Equals(targetLang, "en", StringComparison.OrdinalIgnoreCase);

        // A line built from greetings and slang reads fine translated onward
        // from English; one built from bare jargon would be re-translated
        // literally, so for other targets that goes through the placeholders.
        if (plan.Composed != null && (toEnglish || plan.ComposedFromPhrases))
        {
            DebugLog($"[dudu的書] glossary composed {plan.Hits} line(s): '{plan.Composed}'");
            if (toEnglish)
                return new TranslationResult(plan.Composed, string.Empty, "ja");

            var viaEnglish = await translator.TranslateAsync(
                plan.Composed, "en", targetLang, false, ct).ConfigureAwait(false);
            return viaEnglish == null ? null : viaEnglish with { DetectedSource = "ja" };
        }

        if (plan.Rewritten == null)
            return null;

        DebugLog($"[dudu的書] glossary injected {plan.Terms.Count} term(s): '{plan.Rewritten}'");

        // Into English the terms go in as words, so Google can build its
        // grammar around their meaning ("stack お願いします" → "stack please").
        if (!toEnglish && plan.Shielded != null)
        {
            var shielded = await translator.TranslateAsync(
                plan.Shielded, "ja", targetLang, false, ct).ConfigureAwait(false);
            var restored = shielded == null ? null : Restore(shielded.Translated, plan.Terms);
            if (restored != null)
                return shielded! with { Translated = restored, DetectedSource = "ja" };

            DebugLog("[dudu的書] glossary placeholders did not survive translation; using inline terms");
        }

        var result = await translator.TranslateAsync(
            plan.Rewritten, "ja", targetLang, false, ct).ConfigureAwait(false);
        return result == null ? null : result with { DetectedSource = "ja" };
    }

    public GlossaryPlan Prepare(string text)
    {
        if (Count == 0 || string.IsNullOrWhiteSpace(text))
            return GlossaryPlan.None;

        // NFKC folds full-width Latin and half-width kana (ＰＴ → PT,
        // ﾉｼ → ノシ, ｗｗ → ww) so one entry covers every way it gets typed.
        var normalized = text.Normalize(NormalizationForm.FormKC).Trim();
        var folded = normalized.ToLowerInvariant();

        var composed = Compose(folded, out var lineCount, out var fromPhrases);
        var terms = new List<string>();
        var (rewritten, shielded) = Inject(normalized, folded, terms);

        // Our own X1/X2 can't be told apart from text that already has them.
        if (Placeholder.IsMatch(normalized))
            shielded = null;

        if (composed == null && rewritten == null)
            return GlossaryPlan.None;

        return new GlossaryPlan(composed, fromPhrases, rewritten, shielded, terms,
                                composed != null ? lineCount : terms.Count);
    }

    private string? Compose(string folded, out int count, out bool fromPhrases)
    {
        var parts = new List<string>();
        count = 0;
        fromPhrases = true;

        var start = 0;
        for (var i = 0; i <= folded.Length; i++)
        {
            if (i < folded.Length && !IsSegmentBreak(folded[i]))
                continue;

            var segment = folded[start..i].Trim();
            start = i + 1;
            if (segment.Length == 0)
                continue;

            if (GlossLine(segment) is not { } line)
                return null;
            parts.Add(line.English);
            fromPhrases &= line.IsPhrase;
        }

        count = parts.Count;
        if (parts.Count == 0)
            return null;

        // Sentences already end in punctuation; bare terms ("ready check",
        // "disband after 3 wipes") need a comma or they run together.
        var sb = new StringBuilder(parts[0]);
        for (var i = 1; i < parts.Count; i++)
            sb.Append(parts[i - 1][^1] is '.' or '!' or '?' ? " " : ", ").Append(parts[i]);
        return sb.ToString();
    }

    private Line? GlossLine(string segment)
    {
        if (lines.TryGetValue(segment, out var line))
            return line;

        // A laugh tacked onto a known line (おつw, 了解笑) stays a laugh.
        var core = segment.TrimEnd('w', '笑', '草');
        if (core.Length == segment.Length)
            return null;
        if (core.Length == 0)
            return new Line("lol", true);
        return lines.TryGetValue(core, out line) ? line with { English = $"{line.English} lol" } : null;
    }

    private (string? Rewritten, string? Shielded) Inject(string normalized, string folded, List<string> terms)
    {
        var rewritten = new StringBuilder(normalized.Length + 32);
        var shielded = new StringBuilder(normalized.Length + 16);
        var lastWasTerm = false;

        var i = 0;
        while (i < folded.Length)
        {
            string? english = null;
            var length = 0;

            var laugh = folded[i] == 'w' ? LaughterLength(folded, i) : 0;
            if (laugh > 0)
            {
                english = "lol";
                length = laugh;
            }
            else if (termsByFirstChar.TryGetValue(folded[i], out var candidates))
            {
                foreach (var term in candidates)
                {
                    if (folded.AsSpan(i).StartsWith(term.Key) && Fits(folded, i, term))
                    {
                        english = term.English;
                        length = term.Key.Length;
                        break;
                    }
                }
            }

            if (english != null)
            {
                terms.Add(english);
                // English is spaced off from any letter, Japanese included:
                // fused, Google misreads it ("cofferあればloot-and-leave" came
                // back as "root-and-leave"). Placeholders read more naturally
                // fused and only need room from Latin text (MT〆 → "MT X1").
                AppendSpaced(rewritten, english, char.IsLetterOrDigit);
                AppendSpaced(shielded, $"X{terms.Count}", char.IsAsciiLetterOrDigit);
                lastWasTerm = true;
                i += length;
                continue;
            }

            var c = normalized[i];
            if (lastWasTerm && char.IsLetterOrDigit(c))
                rewritten.Append(' ');
            if (lastWasTerm && char.IsAsciiLetterOrDigit(c))
                shielded.Append(' ');
            rewritten.Append(c);
            shielded.Append(c);
            lastWasTerm = false;
            i++;
        }

        return terms.Count == 0 ? (null, null) : (rewritten.ToString(), shielded.ToString());
    }

    private static void AppendSpaced(StringBuilder sb, string word, Func<char, bool> needsGap)
    {
        if (sb.Length > 0 && needsGap(sb[^1]))
            sb.Append(' ');
        sb.Append(word);
    }

    /// <summary>
    /// Swaps X1, X2… back for their English terms, or returns null when any of
    /// them was dropped or duplicated by the translator.
    /// </summary>
    private static string? Restore(string translated, IReadOnlyList<string> terms)
    {
        var seen = new int[terms.Count];
        var restored = Placeholder.Replace(translated, match =>
        {
            var index = int.Parse(match.Groups[1].Value) - 1;
            if (index < 0 || index >= terms.Count)
                return match.Value;
            seen[index]++;
            return terms[index];
        });
        return seen.All(n => n == 1) ? restored : null;
    }

    private static bool Fits(string s, int start, Term term)
    {
        var end = start + term.Key.Length;
        var prev = start > 0 ? s[start - 1] : '\0';
        var next = end < s.Length ? s[end] : '\0';
        var first = term.Key[0];
        var last = term.Key[^1];

        // Never split a katakana, Latin or hiragana word: ナイト is not a
        // match inside ナイトメア, nor PT inside OPT.
        if (IsKatakana(first) && IsKatakana(prev)) return false;
        if (IsKatakana(last) && IsKatakana(next)) return false;
        if (char.IsAsciiLetterOrDigit(first) && char.IsAsciiLetterOrDigit(prev)) return false;
        if (char.IsAsciiLetterOrDigit(last) && char.IsAsciiLetterOrDigit(next)) return false;
        if (IsHiragana(first) && IsHiragana(prev)) return false;

        if (term.ShortKanji)
        {
            if (IsKanji(prev) && !KanjiPrefixes.Contains(prev)) return false;
            if (IsKanji(next) && !KanjiSuffixes.Contains(next)) return false;
        }

        return term.Next switch
        {
            Follow.Katakana => IsKatakana(next),
            Follow.End => next is '\0' or 'w' || char.IsWhiteSpace(next) ||
                          char.IsPunctuation(next) || char.IsSymbol(next),
            _ => true,
        };
    }

    // ｗｗｗ (www after NFKC) is laughter, but only standing apart from Latin
    // text: never inside "wow" or "www.example". A lone w counts only right
    // after Japanese (了解w), where it can't be an English word.
    private static int LaughterLength(string folded, int start)
    {
        var end = start;
        while (end < folded.Length && folded[end] == 'w')
            end++;

        var prev = start > 0 ? folded[start - 1] : '\0';
        var next = end < folded.Length ? folded[end] : '\0';
        if (char.IsAsciiLetterOrDigit(prev) || char.IsAsciiLetterOrDigit(next) || next == '.')
            return 0;
        if (end - start == 1 && !IsCjk(prev) && prev is not ('!' or '?'))
            return 0;

        return end - start;
    }

    private static string Fold(string s) => s.Normalize(NormalizationForm.FormKC).Trim().ToLowerInvariant();

    private static bool IsSegmentBreak(char c) => char.IsWhiteSpace(c) || SegmentBreaks.Contains(c);

    // ・ (U+30FB) is punctuation, not part of a katakana word.
    private static bool IsKatakana(char c) => c is >= 'ァ' and <= 'ヺ' or >= 'ー' and <= 'ヾ';

    private static bool IsHiragana(char c) => c is >= 'ぁ' and <= 'ゖ' or 'ゝ' or 'ゞ';

    private static bool IsKanji(char c) => c is >= '一' and <= '鿿' or >= '㐀' and <= '䶿' or '々';

    private static bool IsCjk(char c) => c is >= '　' and <= '鿿';

    private static void DebugLog(string message)
    {
        try
        {
            Plugin.Log?.Debug(message);
        }
        catch
        {
            // Plugin services are unavailable in standalone build/test contexts.
        }
    }

    private enum Follow
    {
        Any,
        Katakana,
        End,
    }

    private sealed record Entry(string Japanese, string English, bool IsPhrase, Follow Next);

    private sealed record Term(string Key, string English, Follow Next, bool ShortKanji);

    private readonly record struct Line(string English, bool IsPhrase);
}

/// <summary>
/// What the glossary would do to one message.
/// <see cref="Composed"/>: complete English, set when every segment is a known
/// entry (<see cref="ComposedFromPhrases"/> when none of them is bare jargon).
/// <see cref="Rewritten"/>: the Japanese with English terms inlined, for
/// translating into English. <see cref="Shielded"/>: the same with X1, X2…
/// placeholders standing in for <see cref="Terms"/>, for other targets.
/// </summary>
public sealed record GlossaryPlan(
    string? Composed,
    bool ComposedFromPhrases,
    string? Rewritten,
    string? Shielded,
    IReadOnlyList<string> Terms,
    int Hits)
{
    public static GlossaryPlan None { get; } = new(null, false, null, null, Array.Empty<string>(), 0);
}
