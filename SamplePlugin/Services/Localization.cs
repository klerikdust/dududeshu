using System.Collections.Generic;

namespace SamplePlugin.Services;

public static class Localization
{
    public const string DefaultLocale = "en";

    public static readonly (string Code, string Label)[] AvailableLocales =
    {
        ("en", "English"),
        ("id", "Bahasa Indonesia"),
        ("zh-TW", "繁體中文"),
    };

    public static string Get(string locale, string key)
    {
        if (Strings.TryGetValue(locale, out var table) && table.TryGetValue(key, out var value))
            return value;
        if (Strings[DefaultLocale].TryGetValue(key, out var fallback))
            return fallback;
        return key;
    }

    private static readonly Dictionary<string, Dictionary<string, string>> Strings = new()
    {
        ["en"] = new()
        {
            ["window.title"]              = "dudu's book",
            ["tagline"]                   = "Smart player messages translation tool for FF14",
            ["section.uiLanguage"]        = "UI language",
            ["enable.translator"]         = "Enable translator",
            ["enable.ignoreOwn"]          = "Ignore my own messages",
            ["enable.transliteration"]    = "Show romaji / pinyin in parentheses",
            ["enable.autoSend"]           = "/jp and /zh auto-send the translation",
            ["tooltip.autoSend"] =
                "On: the translated text is sent on the active channel automatically.\n" +
                "Off: the translation is copied to your clipboard and previewed in chat,\n" +
                "so you can paste it yourself with Ctrl+V before pressing Enter.",
            ["enable.useEcho"]            = "Print translations on the Echo channel",
            ["tooltip.useEcho"] =
                "On: translated lines print on the Echo channel (neutral grey).\n" +
                "Off: translated lines print on the same channel as the source,\n" +
                "so FFXIV's chat-log colour for that channel applies automatically.",
            ["section.translateInto"]     = "Translate into",
            ["section.translateFrom"]     = "Translate messages written in",
            ["section.channels"]          = "Channels",
            ["lang.en"]                   = "English",
            ["lang.ja"]                   = "Japanese",
            ["lang.zh-TW"]                = "Chinese (Traditional)",
            ["lang.id"]                   = "Indonesian",
            ["clipboard.prefix"]          = "[dudu的書] ",
            ["clipboard.copied"]          = "Copied to clipboard. Paste with Ctrl+V: ",
            ["clipboard.copyFailed"]      = "Could not copy translation to clipboard: ",
            ["clipboard.translateFailed"] = "Translation failed for: ",
            ["clipboard.usage"]           = "Usage: ",
        },
        ["id"] = new()
        {
            ["window.title"]              = "Buku dudu",
            ["tagline"]                   = "Alat penerjemah pesan pemain untuk FF14",
            ["section.uiLanguage"]        = "Bahasa antarmuka",
            ["enable.translator"]         = "Aktifkan penerjemah",
            ["enable.ignoreOwn"]          = "Abaikan pesan saya sendiri",
            ["enable.transliteration"]    = "Tampilkan romaji / pinyin dalam tanda kurung",
            ["enable.autoSend"]           = "Kirim otomatis terjemahan dari /jp dan /zh",
            ["tooltip.autoSend"] =
                "Aktif: terjemahan langsung dikirim ke saluran chat yang sedang aktif.\n" +
                "Nonaktif: terjemahan disalin ke clipboard dan ditampilkan di chat,\n" +
                "sehingga kamu bisa menempelnya sendiri dengan Ctrl+V sebelum menekan Enter.",
            ["enable.useEcho"]            = "Tampilkan terjemahan di saluran Echo",
            ["tooltip.useEcho"] =
                "Aktif: terjemahan ditampilkan di saluran Echo (warna abu-abu netral).\n" +
                "Nonaktif: terjemahan ditampilkan di saluran yang sama dengan pesan asli,\n" +
                "sehingga warna saluran dari pengaturan FFXIV berlaku otomatis.",
            ["section.translateInto"]     = "Terjemahkan ke",
            ["section.translateFrom"]     = "Terjemahkan pesan dalam bahasa",
            ["section.channels"]          = "Saluran chat",
            ["lang.en"]                   = "Inggris",
            ["lang.ja"]                   = "Jepang",
            ["lang.zh-TW"]                = "Tionghoa (Tradisional)",
            ["lang.id"]                   = "Indonesia",
            ["clipboard.prefix"]          = "[dudu的書] ",
            ["clipboard.copied"]          = "Disalin ke clipboard. Tempel dengan Ctrl+V: ",
            ["clipboard.copyFailed"]      = "Gagal menyalin terjemahan ke clipboard: ",
            ["clipboard.translateFailed"] = "Terjemahan gagal untuk: ",
            ["clipboard.usage"]           = "Cara pakai: ",
        },
        ["zh-TW"] = new()
        {
            ["window.title"]              = "嘟嘟之書",
            ["tagline"]                   = "FF14 玩家訊息智慧翻譯工具",
            ["section.uiLanguage"]        = "介面語言",
            ["enable.translator"]         = "啟用翻譯",
            ["enable.ignoreOwn"]          = "忽略自己發送的訊息",
            ["enable.transliteration"]    = "在括號中顯示羅馬字 / 拼音",
            ["enable.autoSend"]           = "/jp 與 /zh 自動發送翻譯結果",
            ["tooltip.autoSend"] =
                "開啟：翻譯後的訊息會直接送到當前頻道。\n" +
                "關閉：翻譯結果會複製到剪貼簿並在聊天中預覽,\n" +
                "您可以自行用 Ctrl+V 貼上後再按 Enter 送出。",
            ["enable.useEcho"]            = "在 Echo 頻道輸出翻譯訊息",
            ["tooltip.useEcho"] =
                "開啟：翻譯訊息會顯示在 Echo 頻道（中性灰色）。\n" +
                "關閉：翻譯訊息會顯示在與原訊息相同的頻道,\n" +
                "FFXIV 的頻道顏色會自動套用。",
            ["section.translateInto"]     = "翻譯為",
            ["section.translateFrom"]     = "翻譯以下語言的訊息",
            ["section.channels"]          = "頻道",
            ["lang.en"]                   = "英文",
            ["lang.ja"]                   = "日文",
            ["lang.zh-TW"]                = "繁體中文",
            ["lang.id"]                   = "印尼文",
            ["clipboard.prefix"]          = "[dudu的書] ",
            ["clipboard.copied"]          = "已複製到剪貼簿。請用 Ctrl+V 貼上：",
            ["clipboard.copyFailed"]      = "無法將翻譯複製到剪貼簿：",
            ["clipboard.translateFailed"] = "翻譯失敗：",
            ["clipboard.usage"]           = "用法：",
        },
    };
}
