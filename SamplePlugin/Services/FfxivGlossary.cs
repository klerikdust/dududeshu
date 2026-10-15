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
/// those terms are swapped for the words the community uses in the target
/// language first and Google only has to translate the grammar around them.
///
/// Entries live in <c>Data/ffxiv_jp_glossary.json</c>, embedded at build time
/// and compiled from <c>Data/ffxiv_jp_en_dataset.json</c> plus curated
/// additions. Every entry carries a rendering per target language (en, zh-TW,
/// id), so a Chinese or Indonesian translation never falls back to English
/// jargon. Entries come in two kinds:
/// <list type="bullet">
/// <item><b>phrase</b>: a whole chat line (よろしくお願いします, おつ, 草). Used
/// only when every segment of a message is a known entry; the line is then
/// composed directly in the target language and Google never sees it.</item>
/// <item><b>term</b>: jargon injected inside a sentence, with script-boundary
/// checks so it never fires inside a longer word (ナイト in ナイトメア, 線 in
/// 直線).</item>
/// </list>
/// </summary>
public sealed class FfxivGlossary
{
    private const string ResourceName = "dududeshu.ffxiv_jp_glossary.json";
    private const string English = "en";

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
    // sensibly ("請X1", "Setelah X1 adalah X2"), and the target-language term
    // goes back in afterwards. Handing Google the term itself instead gets it
    // re-translated literally (farming → 養殖, wipe → 擦拭).
    private static readonly Regex Placeholder = new(
        @"(?<![A-Za-z])X(\d{1,3})(?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // A space Google leaves between two CJK characters once a placeholder has
    // been swapped back ("如果 裝備箱 則") reads as a typo in Chinese.
    private static readonly Regex CjkGap = new(
        @"(?<=[　-〿぀-ヿ㐀-鿿＀-￯])[ \t]+(?=[　-〿぀-ヿ㐀-鿿＀-￯])",
        RegexOptions.CultureInvariant);

    private static readonly Rendering Lol = new(new Dictionary<string, string> { [English] = "lol" });

    private readonly Dictionary<string, Line> lines = new(StringComparer.Ordinal);
    private readonly Dictionary<char, Term[]> termsByFirstChar;
    private readonly Rendering laugh;

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
                lines[key] = new Line(entry.Text, entry.IsPhrase);
            Count++;

            if (entry.IsPhrase)
                continue;

            if (!terms.TryGetValue(key[0], out var bucket))
                terms[key[0]] = bucket = new List<Term>();
            bucket.Add(new Term(key, entry.Text, entry.Next, key.Length <= 2 && key.All(IsKanji)));
        }

        termsByFirstChar = terms.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.OrderByDescending(term => term.Key.Length).ToArray());

        // The w entry says how a laugh reads in each language (lol, 哈哈, wkwk).
        laugh = lines.TryGetValue("w", out var w) ? w.Text : Lol;
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
            if (string.IsNullOrWhiteSpace(japanese))
                continue;

            // Every other string property is a rendering keyed by language code.
            var text = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in row.EnumerateObject())
            {
                if (property.Name is "ja" or "mode" or "next" or "src" ||
                    property.Value.ValueKind != JsonValueKind.String)
                    continue;
                var value = property.Value.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    text[property.Name] = value.Trim();
            }
            if (!text.ContainsKey(English))
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
            entries.Add(new Entry(japanese, new Rendering(text), isPhrase, next));
        }

        return new FfxivGlossary(entries);
    }

    /// <summary>
    /// Translates Japanese <paramref name="text"/> into
    /// <paramref name="targetLang"/> with the glossary applied. Returns null
    /// when no entry matched, so the caller falls back to its plain
    /// translation path.
    /// </summary>
    public async Task<TranslationResult?> TranslateAsync(
        Translator translator, string text, string targetLang, CancellationToken ct)
    {
        var plan = Prepare(text, targetLang);

        if (plan.Composed != null)
        {
            DebugLog($"[dudu的書] glossary composed {plan.Hits} line(s): '{plan.Composed}'");
            if (!plan.ComposedNeedsTranslation)
                return new TranslationResult(plan.Composed, string.Empty, "ja");

            // A target the glossary has no renderings for: the English lines
            // still translate cleanly as whole sentences.
            var viaEnglish = await translator.TranslateAsync(
                plan.Composed, English, targetLang, false, ct).ConfigureAwait(false);
            return viaEnglish == null ? null : viaEnglish with { DetectedSource = "ja" };
        }

        if (plan.Rewritten == null)
            return null;

        DebugLog($"[dudu的書] glossary injected {plan.Terms.Count} term(s): '{plan.Rewritten}'");

        // Into English the terms go in as words, so Google can build its
        // grammar around their meaning ("stack お願いします" → "stack please").
        if (!IsEnglish(targetLang) && plan.Shielded != null)
        {
            var shielded = await translator.TranslateAsync(
                plan.Shielded, "ja", targetLang, false, ct).ConfigureAwait(false);
            var restored = shielded == null ? null : Restore(shielded.Translated, plan.Terms);
            if (restored != null)
                return shielded! with { Translated = Tidy(restored, targetLang), DetectedSource = "ja" };

            DebugLog("[dudu的書] glossary placeholders did not survive translation; using inline terms");
        }

        var result = await translator.TranslateAsync(
            plan.Rewritten, "ja", targetLang, false, ct).ConfigureAwait(false);
        return result == null ? null : result with { Translated = Tidy(result.Translated, targetLang), DetectedSource = "ja" };
    }

    public GlossaryPlan Prepare(string text, string targetLang)
    {
        if (Count == 0 || string.IsNullOrWhiteSpace(text))
            return GlossaryPlan.None;

        // NFKC folds full-width Latin and half-width kana (ＰＴ → PT,
        // ﾉｼ → ノシ, ｗｗ → ww) so one entry covers every way it gets typed.
        var normalized = text.Normalize(NormalizationForm.FormKC).Trim();
        var folded = normalized.ToLowerInvariant();

        var composed = Compose(folded, targetLang, out var lineCount, out var needsTranslation);
        var terms = new List<string>();
        var (rewritten, shielded) = Inject(normalized, folded, targetLang, terms);

        // Our own X1/X2 can't be told apart from text that already has them.
        if (Placeholder.IsMatch(normalized))
            shielded = null;

        if (composed == null && rewritten == null)
            return GlossaryPlan.None;

        return new GlossaryPlan(composed, needsTranslation, rewritten, shielded, terms,
                                composed != null ? lineCount : terms.Count);
    }

    private string? Compose(string folded, string lang, out int count, out bool needsTranslation)
    {
        var parts = new List<(Line Line, bool Laugh)>();
        count = 0;
        needsTranslation = false;

        var start = 0;
        for (var i = 0; i <= folded.Length; i++)
        {
            if (i < folded.Length && !IsSegmentBreak(folded[i]))
                continue;

            var segment = folded[start..i].Trim();
            start = i + 1;
            if (segment.Length == 0)
                continue;

            if (GlossLine(segment) is not { } part)
                return null;
            parts.Add(part);
        }

        count = parts.Count;
        if (parts.Count == 0)
            return null;

        if (parts.All(p => p.Line.Text.Has(lang) && (!p.Laugh || laugh.Has(lang))))
            return Join(parts, lang);

        // No rendering for this target. Whole English sentences still
        // translate well; bare English jargon would come back literal, so a
        // line made of terms goes through the placeholder path instead.
        if (!parts.All(p => p.Line.IsPhrase))
            return null;
        needsTranslation = true;
        return Join(parts, English);
    }

    private (Line Line, bool Laugh)? GlossLine(string segment)
    {
        if (lines.TryGetValue(segment, out var line))
            return (line, false);

        // A laugh tacked onto a known line (おつw, 了解笑) stays a laugh.
        var core = segment.TrimEnd('w', '笑', '草');
        if (core.Length == segment.Length)
            return null;
        if (core.Length == 0)
            return (new Line(laugh, true), false);
        return lines.TryGetValue(core, out line) ? (line, true) : null;
    }

    private string Join(IReadOnlyList<(Line Line, bool Laugh)> parts, string lang)
    {
        // Chinese and Japanese run sentences together without spaces.
        var cjk = IsCjkLanguage(lang);
        var sb = new StringBuilder();
        foreach (var (line, withLaugh) in parts)
        {
            if (sb.Length > 0)
            {
                // Sentences already end in punctuation; bare terms ("ready
                // check", "disband after 3 wipes") need a comma between them.
                sb.Append(EndsSentence(sb) ? (cjk ? "" : " ") : (cjk ? "，" : ", "));
            }
            sb.Append(line.Text.For(lang));
            if (withLaugh)
                sb.Append(cjk ? "" : " ").Append(laugh.For(lang));
        }
        return sb.ToString();
    }

    // "Bye! (waves)" ends a sentence; "every food (30 min)" doesn't, so a
    // closing parenthesis counts only when the text before it does.
    private static bool EndsSentence(StringBuilder sb)
    {
        var end = sb.Length - 1;
        if (sb[end] is ')' or '）')
        {
            while (end >= 0 && sb[end] is not ('(' or '（'))
                end--;
            end--;
            while (end >= 0 && sb[end] == ' ')
                end--;
            if (end < 0)
                return false;
        }

        return sb[end] is '.' or '!' or '?' or '。' or '！' or '？';
    }

    private (string? Rewritten, string? Shielded) Inject(
        string normalized, string folded, string lang, List<string> terms)
    {
        var rewritten = new StringBuilder(normalized.Length + 32);
        var shielded = new StringBuilder(normalized.Length + 16);
        var lastWasTerm = false;

        var i = 0;
        while (i < folded.Length)
        {
            Rendering? text = null;
            var length = 0;

            var laughLength = folded[i] == 'w' ? LaughterLength(folded, i) : 0;
            if (laughLength > 0)
            {
                text = laugh;
                length = laughLength;
            }
            else if (termsByFirstChar.TryGetValue(folded[i], out var candidates))
            {
                foreach (var term in candidates)
                {
                    if (folded.AsSpan(i).StartsWith(term.Key) && Fits(folded, i, term))
                    {
                        text = term.Text;
                        length = term.Key.Length;
                        break;
                    }
                }
            }

            if (text != null)
            {
                var word = text.For(lang);
                terms.Add(word);
                // Inline words are spaced off from any letter, Japanese
                // included: fused, Google misreads them ("cofferあればloot-
                // and-leave" came back as "root-and-leave"). Placeholders read
                // more naturally fused and only need room from Latin text
                // (MT〆 → "MT X1").
                AppendSpaced(rewritten, word, char.IsLetterOrDigit);
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
    /// Swaps X1, X2… back for their terms, or returns null when any of them
    /// was dropped or duplicated by the translator.
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

    private static string Tidy(string translated, string lang) =>
        IsCjkLanguage(lang) ? CjkGap.Replace(translated, string.Empty) : translated;

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

    private static bool IsEnglish(string lang) => string.Equals(lang, English, StringComparison.OrdinalIgnoreCase);

    private static bool IsCjkLanguage(string lang) =>
        lang.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ||
        lang.StartsWith("ja", StringComparison.OrdinalIgnoreCase);

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

    /// <summary>One glossary meaning, written out per target language.</summary>
    private sealed class Rendering
    {
        private readonly Dictionary<string, string> byLanguage;

        public Rendering(Dictionary<string, string> byLanguage) => this.byLanguage = byLanguage;

        public bool Has(string lang) => byLanguage.ContainsKey(lang);

        public string For(string lang) => byLanguage.TryGetValue(lang, out var text) ? text : byLanguage[English];
    }

    private sealed record Entry(string Japanese, Rendering Text, bool IsPhrase, Follow Next);

    private sealed record Term(string Key, Rendering Text, Follow Next, bool ShortKanji);

    private readonly record struct Line(Rendering Text, bool IsPhrase);
}

/// <summary>
/// What the glossary would do to one message, for one target language.
/// <see cref="Composed"/>: the whole message, set when every segment is a
/// known entry; already in the target language unless
/// <see cref="ComposedNeedsTranslation"/> (English for a target the glossary
/// has no renderings for). <see cref="Rewritten"/>: the Japanese with the
/// target-language <see cref="Terms"/> inlined. <see cref="Shielded"/>: the
/// same with X1, X2… placeholders standing in for them.
/// </summary>
public sealed record GlossaryPlan(
    string? Composed,
    bool ComposedNeedsTranslation,
    string? Rewritten,
    string? Shielded,
    IReadOnlyList<string> Terms,
    int Hits)
{
    public static GlossaryPlan None { get; } = new(null, false, null, null, Array.Empty<string>(), 0);
}
