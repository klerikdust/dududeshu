using System.Linq;

namespace SamplePlugin.Services;

public static class LanguageDetector
{
    public static string Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "und";

        var hasHiraganaKatakana = false;
        var hasHan = false;
        var hasHangul = false;
        var hasLatin = false;
        var traditionalScore = 0;
        var simplifiedScore = 0;

        foreach (var ch in text)
        {
            // Hiragana / Katakana / half-width katakana
            if ((ch >= 0x3040 && ch <= 0x309F) ||
                (ch >= 0x30A0 && ch <= 0x30FF) ||
                (ch >= 0xFF66 && ch <= 0xFF9F))
            {
                hasHiraganaKatakana = true;
            }
            else if (ch >= 0x4E00 && ch <= 0x9FFF)
            {
                hasHan = true;
                if (TraditionalOnly.Contains(ch)) traditionalScore++;
                if (SimplifiedOnly.Contains(ch)) simplifiedScore++;
            }
            else if (ch >= 0xAC00 && ch <= 0xD7AF)
            {
                hasHangul = true;
            }
            else if ((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z'))
            {
                hasLatin = true;
            }
        }

        if (hasHiraganaKatakana) return "ja";
        if (hasHan)
        {
            // We only support Traditional in the UI, so route any Han-only run there.
            // Google Translate will still detect simplified vs traditional internally.
            return "zh-TW";
        }
        if (hasHangul) return "ko";
        if (hasLatin) return "en";

        return "und";
    }

    public static bool NeedsTransliteration(string lang) =>
        lang == "ja" || lang == "zh-TW" || lang == "zh-CN" || lang == "ko";

    // Tiny set of characters that only exist in one script form. Not exhaustive,
    // but enough to disambiguate most chat lines without shipping a full table.
    private static readonly System.Collections.Generic.HashSet<char> TraditionalOnly = new(
        "說話誰時間問題實際應該麼這個們來對沒們從還會給經過點"
            .Where(c => c >= 0x4E00));

    private static readonly System.Collections.Generic.HashSet<char> SimplifiedOnly = new(
        "说话谁时间问题实际应该么这个们来对没们从还会给经过点"
            .Where(c => c >= 0x4E00));
}
