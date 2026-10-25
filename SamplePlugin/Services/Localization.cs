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
            ["enable.collect"]            = "Collect Japanese lines to improve the glossary",
            ["tooltip.collect"] =
                "On: short Japanese chat lines that get translated are saved on this PC,\n" +
                "so the ones players keep using can be reviewed and added to your own\n" +
                "glossary. Only the line, how often it was seen and Google's translation\n" +
                "are kept; player names are not stored and nothing is uploaded.\n" +
                "Off: nothing is collected.",
            ["button.review"]             = "Review collected lines ({0})",
            ["review.intro"] =
                "Japanese lines seen in chat. Lines used by several players are listed first.\n" +
                "Pick one, write how it should read in each language, and add it to your glossary.",
            ["review.stats"]              = "{0} lines collected, {1} used by several players",
            ["review.off"]                = "Collection is off. Turn it on in the settings window.",
            ["review.showAll"]            = "Show lines seen only once or twice",
            ["review.clear"]              = "Clear collected lines",
            ["review.export"]             = "Copy my glossary (JSON)",
            ["tooltip.export"] =
                "Copies your glossary entries to the clipboard in the same format as the\n" +
                "built-in glossary, so they can be shared and added to the plugin.",
            ["review.exported"]           = "Copied to clipboard.",
            ["review.exportFailed"]       = "Could not copy to clipboard.",
            ["review.tab.collected"]      = "Collected",
            ["review.tab.mine"]           = "My glossary ({0})",
            ["review.empty"]              = "Nothing collected yet.",
            ["review.mineEmpty"]          = "No entries yet. Add one from the Collected tab.",
            ["review.pick"]               = "Select a line on the left.",
            ["review.seen"]               = "Seen {0}× from {1} player(s)",
            ["review.google"]             = "Google ({0}): ",
            ["review.japanese"]           = "Japanese",
            ["review.phrase"]             = "Whole line",
            ["tooltip.phrase"] =
                "On: used when a message is exactly this line (like よろしく or おつ).\n" +
                "Off: used as a term inside longer sentences (like 頭割り).",
            ["review.fill"]               = "Fill empty fields with Google",
            ["review.filling"]            = "Translating…",
            ["review.add"]                = "Add to my glossary",
            ["review.save"]               = "Save",
            ["review.dismiss"]            = "Dismiss",
            ["tooltip.dismiss"]           = "Remove this line and never collect it again.",
            ["review.delete"]             = "Delete",
            ["review.needEnglish"]        = "English is required.",
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
            ["section.chat"]              = "Chat",
            ["section.reading"]           = "Pronunciation",
            ["tab.general"]               = "General",
            ["tab.languages"]             = "Languages",
            ["tab.glossary"]              = "Glossary",
            ["status.on"]                 = "Translating chat",
            ["status.off"]                = "Paused",
            ["channels.count"]            = "{0} of {1} channels on",
            ["channels.all"]              = "All",
            ["channels.none"]             = "None",
            ["glossary.stats"]            = "{0} collected lines · {1} entries in my glossary",
            ["titlebar.settings"]         = "Settings",
            ["review.turnOn"]             = "Turn on",
            ["review.added"]              = "Added to my glossary.",
            ["tooltip.clear"]             = "Hold Ctrl and click to clear.",
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
            ["enable.collect"]            = "Kumpulkan kalimat Jepang untuk memperbaiki glosarium",
            ["tooltip.collect"] =
                "Aktif: kalimat chat Jepang pendek yang diterjemahkan disimpan di PC ini,\n" +
                "agar kalimat yang sering dipakai pemain bisa ditinjau dan ditambahkan ke\n" +
                "glosarium milikmu. Yang disimpan hanya kalimatnya, berapa kali muncul, dan\n" +
                "terjemahan Google; nama pemain tidak disimpan dan tidak ada yang diunggah.\n" +
                "Nonaktif: tidak ada yang dikumpulkan.",
            ["button.review"]             = "Tinjau kalimat terkumpul ({0})",
            ["review.intro"] =
                "Kalimat Jepang yang muncul di chat. Kalimat yang dipakai banyak pemain tampil duluan.\n" +
                "Pilih satu, tulis artinya di tiap bahasa, lalu tambahkan ke glosariummu.",
            ["review.stats"]              = "{0} kalimat terkumpul, {1} dipakai beberapa pemain",
            ["review.off"]                = "Pengumpulan nonaktif. Aktifkan di jendela pengaturan.",
            ["review.showAll"]            = "Tampilkan kalimat yang baru muncul sekali atau dua kali",
            ["review.clear"]              = "Hapus kalimat terkumpul",
            ["review.export"]             = "Salin glosariumku (JSON)",
            ["tooltip.export"] =
                "Menyalin entri glosariummu ke clipboard dengan format yang sama seperti\n" +
                "glosarium bawaan, supaya bisa dibagikan dan ditambahkan ke plugin.",
            ["review.exported"]           = "Disalin ke clipboard.",
            ["review.exportFailed"]       = "Gagal menyalin ke clipboard.",
            ["review.tab.collected"]      = "Terkumpul",
            ["review.tab.mine"]           = "Glosariumku ({0})",
            ["review.empty"]              = "Belum ada yang terkumpul.",
            ["review.mineEmpty"]          = "Belum ada entri. Tambahkan dari tab Terkumpul.",
            ["review.pick"]               = "Pilih kalimat di sebelah kiri.",
            ["review.seen"]               = "Muncul {0}× dari {1} pemain",
            ["review.google"]             = "Google ({0}): ",
            ["review.japanese"]           = "Jepang",
            ["review.phrase"]             = "Satu kalimat utuh",
            ["tooltip.phrase"] =
                "Aktif: dipakai saat pesan persis berisi kalimat ini (seperti よろしく atau おつ).\n" +
                "Nonaktif: dipakai sebagai istilah di dalam kalimat panjang (seperti 頭割り).",
            ["review.fill"]               = "Isi kolom kosong dengan Google",
            ["review.filling"]            = "Menerjemahkan…",
            ["review.add"]                = "Tambahkan ke glosariumku",
            ["review.save"]               = "Simpan",
            ["review.dismiss"]            = "Abaikan",
            ["tooltip.dismiss"]           = "Hapus kalimat ini dan jangan kumpulkan lagi.",
            ["review.delete"]             = "Hapus",
            ["review.needEnglish"]        = "Bahasa Inggris wajib diisi.",
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
            ["section.chat"]              = "Chat",
            ["section.reading"]           = "Cara baca",
            ["tab.general"]               = "Umum",
            ["tab.languages"]             = "Bahasa",
            ["tab.glossary"]              = "Glosarium",
            ["status.on"]                 = "Menerjemahkan chat",
            ["status.off"]                = "Dijeda",
            ["channels.count"]            = "{0} dari {1} saluran aktif",
            ["channels.all"]              = "Semua",
            ["channels.none"]             = "Kosongkan",
            ["glossary.stats"]            = "{0} kalimat terkumpul · {1} entri di glosariumku",
            ["titlebar.settings"]         = "Pengaturan",
            ["review.turnOn"]             = "Aktifkan",
            ["review.added"]              = "Ditambahkan ke glosariumku.",
            ["tooltip.clear"]             = "Tahan Ctrl lalu klik untuk menghapus.",
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
            ["enable.collect"]            = "收集日文訊息以改善術語表",
            ["tooltip.collect"] =
                "開啟：被翻譯的簡短日文聊天訊息會儲存在這台電腦上，\n" +
                "方便檢視玩家常用的說法並加入您自己的術語表。\n" +
                "只保存訊息本身、出現次數與 Google 翻譯；不保存玩家名稱，也不會上傳。\n" +
                "關閉：不收集任何訊息。",
            ["button.review"]             = "檢視收集到的訊息（{0}）",
            ["review.intro"] =
                "聊天中出現的日文訊息，多位玩家使用過的排在前面。\n" +
                "選擇一則，填寫各語言的說法，再加入您的術語表。",
            ["review.stats"]              = "已收集 {0} 則，其中 {1} 則有多位玩家使用",
            ["review.off"]                = "收集功能已關閉，請在設定視窗中開啟。",
            ["review.showAll"]            = "顯示只出現一兩次的訊息",
            ["review.clear"]              = "清除收集到的訊息",
            ["review.export"]             = "複製我的術語表（JSON）",
            ["tooltip.export"] =
                "以與內建術語表相同的格式將您的條目複製到剪貼簿，\n" +
                "方便分享並加入外掛。",
            ["review.exported"]           = "已複製到剪貼簿。",
            ["review.exportFailed"]       = "無法複製到剪貼簿。",
            ["review.tab.collected"]      = "收集到的",
            ["review.tab.mine"]           = "我的術語表（{0}）",
            ["review.empty"]              = "尚未收集到任何訊息。",
            ["review.mineEmpty"]          = "尚無條目，請從「收集到的」分頁加入。",
            ["review.pick"]               = "請從左側選擇一則訊息。",
            ["review.seen"]               = "出現 {0} 次，來自 {1} 位玩家",
            ["review.google"]             = "Google（{0}）：",
            ["review.japanese"]           = "日文",
            ["review.phrase"]             = "整句",
            ["tooltip.phrase"] =
                "開啟：訊息完全是這句話時使用（如 よろしく、おつ）。\n" +
                "關閉：作為長句中的術語使用（如 頭割り）。",
            ["review.fill"]               = "用 Google 填入空白欄位",
            ["review.filling"]            = "翻譯中…",
            ["review.add"]                = "加入我的術語表",
            ["review.save"]               = "儲存",
            ["review.dismiss"]            = "忽略",
            ["tooltip.dismiss"]           = "移除這則訊息，之後也不再收集。",
            ["review.delete"]             = "刪除",
            ["review.needEnglish"]        = "必須填寫英文。",
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
            ["section.chat"]              = "聊天",
            ["section.reading"]           = "讀音",
            ["tab.general"]               = "一般",
            ["tab.languages"]             = "語言",
            ["tab.glossary"]              = "術語表",
            ["status.on"]                 = "正在翻譯聊天",
            ["status.off"]                = "已暫停",
            ["channels.count"]            = "已開啟 {0} / {1} 個頻道",
            ["channels.all"]              = "全選",
            ["channels.none"]             = "全不選",
            ["glossary.stats"]            = "已收集 {0} 則 · 我的術語表 {1} 條",
            ["titlebar.settings"]         = "設定",
            ["review.turnOn"]             = "開啟",
            ["review.added"]              = "已加入我的術語表。",
            ["tooltip.clear"]             = "按住 Ctrl 並點擊以清除。",
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
