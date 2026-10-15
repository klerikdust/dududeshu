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
            ["enable.translateEmotes"]     = "Translate emote messages",
            ["tooltip.translateEmotes"] =
                "On: custom and standard emote lines are translated.\n" +
                "Off: emote lines are ignored.",
            ["enable.romaji"]            = "Show romaji",
            ["enable.pinyin"]            = "Show pinyin",
            ["enable.glossary"]           = "Use FFXIV glossary for Japanese",
            ["tooltip.glossary"] =
                "On: before Japanese text is translated, FFXIV jargon (raid callouts,\n" +
                "Party Finder shorthand, chat slang) is replaced with the terms players\n" +
                "use in your target language, e.g. 頭割り → stack, 消化 → weekly reclear,\n" +
                "野良 → PUG (分攤 / 每週通關 / 野團 when translating into Chinese).\n" +
                "Common lines like よろしくお願いします or おつ are translated directly.\n" +
                "Only applies when the text being translated is Japanese.\n" +
                "Off: Japanese text goes to the translator unchanged.",
            ["enable.autoSend"]           = "/jp and /zh auto-send the translation",
            ["tooltip.autoSend"] =
                "On: the translated text is sent on the active channel automatically.\n" +
                "Off: the translation is copied to your clipboard and previewed in chat,\n" +
                "so you can paste it yourself with Ctrl+V before pressing Enter.",
            ["enable.useEcho"]            = "Print translations on the Echo channel",
            ["tooltip.useEcho"] =
                "On: translated lines print on the Echo channel (neutral grey).\n" +
                "Off: translated lines print on the same channel as the source,\n" +
                "so FFXIV's chat-log colour for that channel applies automatically.\n" +
                "\n" +
                "Tip: to recolour Echo lines, go to\n" +
                "Character Configuration → Log Window → Log Text Color\n" +
                "→ Announcement → Echo.",
            ["enable.translatePfMenu"]    = "Add 'Translate' to Party Finder right-click menu",
            ["tooltip.translatePfMenu"] =
                "Right-click inside the Party Finder Detail window and pick\n" +
                "Translate to send the description through the translator\n" +
                "and print the result in your Echo channel.",
            ["pfMenu.translate"]          = "Translate",
            ["pf.sender"]                 = "PF",
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
            ["enable.translateEmotes"]     = "Terjemahkan pesan emote",
            ["tooltip.translateEmotes"] =
                "Aktif: baris emote custom dan standar ikut diterjemahkan.\n" +
                "Nonaktif: baris emote diabaikan.",
            ["enable.romaji"]            = "Tampilkan romaji",
            ["enable.pinyin"]            = "Tampilkan pinyin",
            ["enable.glossary"]           = "Gunakan glosarium FFXIV untuk bahasa Jepang",
            ["tooltip.glossary"] =
                "Aktif: sebelum teks Jepang diterjemahkan, istilah FFXIV (callout raid,\n" +
                "singkatan Party Finder, slang chat) diganti dengan istilah yang dipakai\n" +
                "pemain dalam bahasa tujuanmu, misalnya 頭割り → berkumpul,\n" +
                "消化 → tamat ulang mingguan, 野良 → party acak.\n" +
                "Kalimat umum seperti よろしくお願いします atau おつ langsung diterjemahkan.\n" +
                "Hanya berlaku jika teks yang diterjemahkan berbahasa Jepang.\n" +
                "Nonaktif: teks Jepang dikirim ke penerjemah apa adanya.",
            ["enable.autoSend"]           = "Kirim otomatis terjemahan dari /jp dan /zh",
            ["tooltip.autoSend"] =
                "Aktif: terjemahan langsung dikirim ke saluran chat yang sedang aktif.\n" +
                "Nonaktif: terjemahan disalin ke clipboard dan ditampilkan di chat,\n" +
                "sehingga kamu bisa menempelnya sendiri dengan Ctrl+V sebelum menekan Enter.",
            ["enable.useEcho"]            = "Tampilkan terjemahan di saluran Echo",
            ["tooltip.useEcho"] =
                "Aktif: terjemahan ditampilkan di saluran Echo (warna abu-abu netral).\n" +
                "Nonaktif: terjemahan ditampilkan di saluran yang sama dengan pesan asli,\n" +
                "sehingga warna saluran dari pengaturan FFXIV berlaku otomatis.\n" +
                "\n" +
                "Tips: untuk mengubah warna baris Echo, buka\n" +
                "Character Configuration → Log Window → Log Text Color\n" +
                "→ Announcement → Echo.",
            ["enable.translatePfMenu"]    = "Tambahkan 'Translate' ke menu klik kanan Party Finder",
            ["tooltip.translatePfMenu"] =
                "Klik kanan di dalam jendela Party Finder Detail lalu pilih\n" +
                "Translate untuk mengirim deskripsi ke penerjemah dan\n" +
                "menampilkan hasilnya di saluran Echo kamu.",
            ["pfMenu.translate"]          = "Terjemahkan",
            ["pf.sender"]                 = "PF",
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
            ["enable.translateEmotes"]     = "翻譯情感動作訊息",
            ["tooltip.translateEmotes"] =
                "開啟：會翻譯自訂與標準情感動作訊息。\n" +
                "關閉：忽略情感動作訊息。",
            ["enable.romaji"]            = "顯示羅馬字",
            ["enable.pinyin"]            = "顯示拼音",
            ["enable.glossary"]           = "日文訊息使用 FF14 術語表",
            ["tooltip.glossary"] =
                "開啟：翻譯日文前，先把 FF14 日本玩家的術語（攻略喊話、招募簡稱、\n" +
                "聊天用語）換成目標語言中玩家慣用的說法，例如 頭割り → 分攤、\n" +
                "消化 → 每週通關、野良 → 野團。\n" +
                "よろしくお願いします、おつ 等常用語會直接翻譯。\n" +
                "僅在被翻譯的文字為日文時生效。\n" +
                "關閉：日文原文直接交給翻譯器。",
            ["enable.autoSend"]           = "/jp 與 /zh 自動發送翻譯結果",
            ["tooltip.autoSend"] =
                "開啟：翻譯後的訊息會直接送到當前頻道。\n" +
                "關閉：翻譯結果會複製到剪貼簿並在聊天中預覽,\n" +
                "您可以自行用 Ctrl+V 貼上後再按 Enter 送出。",
            ["enable.useEcho"]            = "在 Echo 頻道輸出翻譯訊息",
            ["tooltip.useEcho"] =
                "開啟：翻譯訊息會顯示在 Echo 頻道（中性灰色）。\n" +
                "關閉：翻譯訊息會顯示在與原訊息相同的頻道,\n" +
                "FFXIV 的頻道顏色會自動套用。\n" +
                "\n" +
                "小提示：若想自訂 Echo 行的顏色，請前往\n" +
                "角色設定 → 日誌視窗設定 → 日誌文字顏色設定\n" +
                "→ 通知 → 默念。",
            ["enable.translatePfMenu"]    = "在招募右鍵選單加入 Translate",
            ["tooltip.translatePfMenu"] =
                "在招募詳情視窗按右鍵後選擇 Translate，\n" +
                "即可將留言送去翻譯並顯示在 Echo 頻道。",
            ["pfMenu.translate"]          = "翻譯",
            ["pf.sender"]                 = "招募",
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
