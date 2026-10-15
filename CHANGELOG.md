# Changelog

## 0.1.24.0

- **FF14 terms now follow your target language.** Translating Japanese into Chinese or Indonesian no longer leaves English jargon in the result: every glossary entry has its own Chinese and Indonesian wording, using the terms players actually use. For example:
  - 安置どこ？ → 「安全點在哪裡？」 / "Dimana titik aman?" (was 「safe spot在哪裡？」 / "Dimana safe spot?")
  - 野良でクリアしました！ → 「用野團通關！」 / "Diselesaikan dengan party acak!" (was 「用PUG通關！」 / "…dengan PUG!")
  - Terms such as 頭割り → 分攤 / berkumpul, 消化 → 每週通關 / tamat ulang mingguan
- Common lines such as よろしくお願いします, お疲れ様でした or 草 are written directly in your target language (你好，請多指教，一起加油！ / "Halo, mohon kerja samanya ya!"), with no extra round-trip through Google.
- Primal names (シヴァ, イフリート, タイタン, ガルーダ…) now come out under their in-game names, e.g. 希瓦 in Chinese rather than 濕婆.
- English translations are unchanged.

## 0.1.23.0

- **The translator is now integrated with FF14 terms and common phrases.** Japanese raid callouts, Party Finder shorthand and chat slang (頭割り, 散開, 消化, クリ目, 野良, 〆, 草…) are translated with a built-in FF14 glossary instead of Google's literal reading. For example:
  - 頭割りお願いします → "stack please" (was "Please split the head")
  - 鯖移動しました → "server transfer done" (was "The mackerel has moved")
- Common FF14 chat lines such as よろしくお願いします, お疲れ様でした, ドンマイ and ノシ are translated directly with the meaning they have in game.
- Only applies when the text being translated is Japanese: incoming chat, Party Finder descriptions (right-click → Translate) and `/en` / `/zh` input. Every other language is translated exactly as before.
- When translating into Chinese or Indonesian, FF14 jargon is kept in English (`請stack`, `Dimana safe spot?`) instead of being re-translated literally.
- New setting **Use FFXIV glossary for Japanese** in `/ducfg`, on by default.
- The glossary has 587 entries. It is built from [`Data/ffxiv_jp_en_dataset.json`](Data/ffxiv_jp_en_dataset.json), a 1,128-entry JP↔EN FF14 reference compiled from community guides, plus curated additions.

Earlier versions: see the [release history](https://github.com/klerikdust/dududeshu/releases).
