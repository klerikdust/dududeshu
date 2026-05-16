# dudu的書

Smart player-messages translation tool for FF14, built on top of the Dalamud API.

dudu的書 listens to the chat channels you choose and echoes a translation back in your target language. Japanese lines get romaji and Chinese lines get pinyin in parentheses. Two extra slash commands, `/jp` and `/zh`, translate the rest of the line into Japanese or Traditional Chinese and send it on your active channel.

> Released under the [AGPL-3.0-or-later](LICENSE.md). This is a Dalamud plugin and runs as a third-party addon to FFXIV; use at your own discretion.

---

## Features

- Auto-translates incoming chat in your enabled channels and echoes the result back in chat.
- Romaji for Japanese and pinyin for Chinese, shown in parentheses next to the translation. Works whether the source is JP/ZH **or** the target is JP/ZH.
- `/jp <text>` -> translates the text into Japanese and sends it on your active channel.
- `/zh <text>` -> translates into Traditional Chinese and sends it on your active channel.
- Auto-send toggle: instead of sending, the plugin can copy the translation to your clipboard and preview it in chat so you can paste it yourself with `Ctrl+V`.
- Source-language filter (English / Japanese / Traditional Chinese).
- Tiny config window (`/ducfg`, `/duconfig`, or `/duduconfig`)

---

## Commands

| Command | What it does |
|---|---|
| `/ducfg`, `/duconfig`, `/duduconfig` | Open the dudu的書 settings window. |
| `/jp <message>` | Translate `<message>` into Japanese and send it on the currently active chat channel. |
| `/zh <message>` | Translate `<message>` into Traditional Chinese and send it on the currently active chat channel. |

You can prefix the message with another channel command if you want to override your active channel. For example: `/jp /p let's pull` translates "let's pull" into japanese and sends it to party.

---

## Installation

dudu的書 is **not on the official Dalamud repository**. To install it you add this repo's manifest URL as a custom Dalamud plugin source. This is sometimes called a "third-party repo" or "experimental repo" in Dalamud.

### Step 1 : Make sure Dalamud is up and running

1. Install [XIVLauncher](https://goatcorp.github.io/) and use it to launch FFXIV at least once with Dalamud enabled.
2. Once you're in the game, type `/xlsettings` in chat to confirm Dalamud is alive.

### Step 2 : Add the dudu的書 custom repository

1. In game, type `/xlsettings`.
2. Go to the **Experimental** tab.
3. Scroll down to **Custom Plugin Repositories**.
4. Paste the manifest URL into the empty text box and click the **+** button:
   ```
   https://raw.githubusercontent.com/klerikdust/dududeshu/master/repo.json
   ```
5. Tick the **Enabled** checkbox next to the new row.
6. Click **Save and Close** at the bottom of the settings window.

### Step 3 — install the plugin

1. Type `/xlplugins` in chat.
2. Go to the **All Plugins** tab and search for `dudu的書` (or just `dudu` should pop up).
3. Click **Install**.
4. After it installs, click it in the list and hit **Open Configuration** or type `/ducfg` in chat to set your channels and target language.

### Updating

Dalamud will check the custom repo automatically on launch. To force a refresh, open `/xlplugins`, hit the refresh icon at the top, and any newer version will install on next game start.

### Uninstalling

`/xlplugins` -> **Installed Plugins** -> find `dudu的書` -> **Uninstall**. To stop pulling updates entirely, remove the custom repo URL from `/xlsettings → Experimental`.

---

## Configuration



- **Enable translator** : toggle on/off for incoming-chat translation.
- **Ignore my own messages** : skip messages you sent yourself.
- **Show romaji / pinyin in parentheses** : append the romanization to the translation.
- **/jp and /zh auto-send the translation** : when on, the translated text is sent immediately on your active channel. When off, it's copied to your clipboard and previewed in an Echo line so you can paste with `Ctrl+V` and confirm before pressing Enter.
- **Translate into** : your target language (English, Japanese, Traditional Chinese).
- **Translate messages written in** : the source languages you want to be translated. Lines in your target language are skipped automatically.
- **Channels** : which chat channels that plugin should translate.

---

## Notes and limitations

- **Translation backend.** Translations come from Google's public `translate.googleapis.com/translate_a/single` endpoint. It works without an API key but is unofficial; if it ever rate-limits or changes shape, swap `SamplePlugin/Services/Translator.cs` for DeepL, Azure, or self-hosted LibreTranslate.
- **Game ToS.** Square Enix officially prohibits all third-party tools; running Dalamud already accepts that risk. dudu的書 does not send anything to the FFXIV server that the game wouldn't normally produce, `/jp` and `/zh` go through the same `RaptureShellModule.ExecuteCommandInner` path as anything you type into the chat box yourself.

---

## License

Source: [AGPL-3.0-or-later](LICENSE.md). Use, modify, and redistribute under those terms.
