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
        var hasBopomofo = false;
        var hasHangul = false;
        var hasLatin = false;

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
            }
            else if ((ch >= 0x3100 && ch <= 0x312F) ||
                     (ch >= 0x31A0 && ch <= 0x31BF))
            {
                hasBopomofo = true;
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
        if (hasHan || hasBopomofo)
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
}
