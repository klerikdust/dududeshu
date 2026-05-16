using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Chat;
using Dalamud.Game.Command;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using SamplePlugin.Services;
using SamplePlugin.Windows;

namespace SamplePlugin;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private static readonly string[] CommandNames = { "/ducfg", "/duconfig", "/duduconfig" };
    private const string SendJaCommand = "/jp";
    private const string SendZhCommand = "/zh";
    private const string SendEnCommand = "/en";

    public Configuration Configuration { get; }
    public WindowSystem WindowSystem { get; } = new("DuduBook");

    private readonly ConfigWindow configWindow;
    private readonly Translator translator;
    private readonly CancellationTokenSource cts = new();

    public string DuduImagePath { get; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        DuduImagePath = System.IO.Path.Combine(
            PluginInterface.AssemblyLocation.Directory?.FullName ?? string.Empty, "dudu.png");

        translator = new Translator();
        configWindow = new ConfigWindow(this);
        WindowSystem.AddWindow(configWindow);

        for (var i = 0; i < CommandNames.Length; i++)
        {
            var name = CommandNames[i];
            CommandManager.AddHandler(name, new CommandInfo(OnCommand)
            {
                HelpMessage = i == 0 ? "Open dudu的書 settings." : string.Empty,
                ShowInHelp = i == 0,
            });
        }

        CommandManager.AddHandler(SendJaCommand, new CommandInfo(OnTranslateAndCopy)
        {
            HelpMessage = "Translate the rest of the line into Japanese and copy it to the clipboard.",
        });
        CommandManager.AddHandler(SendZhCommand, new CommandInfo(OnTranslateAndCopy)
        {
            HelpMessage = "Translate the rest of the line into Traditional Chinese and copy it to the clipboard.",
        });
        CommandManager.AddHandler(SendEnCommand, new CommandInfo(OnTranslateAndCopy)
        {
            HelpMessage = "Translate the rest of the line into English and copy it to the clipboard.",
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleConfigUi;

        ChatGui.ChatMessage += OnChatMessage;
    }

    public void Dispose()
    {
        ChatGui.ChatMessage -= OnChatMessage;
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleConfigUi;

        WindowSystem.RemoveAllWindows();
        configWindow.Dispose();

        CommandManager.RemoveHandler(CommandNames[0]);
        for (var i = 1; i < CommandNames.Length; i++)
            CommandManager.RemoveHandler(CommandNames[i]);
        CommandManager.RemoveHandler(SendJaCommand);
        CommandManager.RemoveHandler(SendZhCommand);
        CommandManager.RemoveHandler(SendEnCommand);

        try { cts.Cancel(); } catch { /* ignore */ }
        cts.Dispose();
        translator.Dispose();
    }

    public void ToggleConfigUi() => configWindow.Toggle();

    private void OnCommand(string command, string args) => ToggleConfigUi();

    private void OnTranslateAndCopy(string command, string args)
    {
        var text = (args ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(text))
        {
            ChatGui.PrintError($"{T("clipboard.prefix")}{T("clipboard.usage")}{command} <message>");
            return;
        }

        var target = command switch
        {
            SendJaCommand => "ja",
            SendZhCommand => "zh-TW",
            SendEnCommand => "en",
            _ => "en",
        };

        var detected = LanguageDetector.Detect(text);
        if (detected == "und") detected = "auto";

        var ct = cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await translator.TranslateAsync(
                    text, detected, target, false, ct).ConfigureAwait(false);

                var translated = result?.Translated;
                if (string.IsNullOrWhiteSpace(translated))
                {
                    await Framework.RunOnFrameworkThread(() =>
                        ChatGui.PrintError($"{T("clipboard.prefix")}{T("clipboard.translateFailed")}{text}"));
                    return;
                }

                // Clipboard work isn't framework-thread sensitive and may block
                // briefly waiting for the OS clipboard, so do it before we
                // marshal back to the framework thread for the chat echo.
                var copied = TryCopyToClipboard(translated);

                await Framework.RunOnFrameworkThread(() =>
                {
                    if (!copied)
                    {
                        ChatGui.PrintError($"{T("clipboard.prefix")}{T("clipboard.copyFailed")}{translated}");
                        return;
                    }

                    var preview = new SeStringBuilder()
                        .AddUiForeground(T("clipboard.prefix"), 52)
                        .AddText(T("clipboard.copied"))
                        .AddUiForeground(translated, 60)
                        .Build();
                    ChatGui.Print(new XivChatEntry { Type = XivChatType.Echo, Message = preview });
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Warning($"[dudu的書] {command} failed: {ex.Message}");
            }
        }, ct);
    }

    private string T(string key) => Localization.Get(Configuration.UiLanguage, key);

    private void OnChatMessage(IHandleableChatMessage message)
    {
        if (!Configuration.Enabled)
            return;

        var type = message.LogKind;
        var senderName = ExtractCleanSenderName(message.Sender);
        var text = ExtractPlainText(message.Message);

        if (string.IsNullOrWhiteSpace(text))
            return;

        // Defensive: don't recurse on our own translated output. Our echoes
        // either set Name to "[XX] Sender" or prefix the message body with
        // "[XX] ". Either pattern starting with [LANG] is enough to skip.
        if (LooksLikeOurEcho(senderName, text))
            return;

        var preview = text.Length > 60 ? text[..60] + "…" : text;
        Log.Debug($"[dudu的書] in: type={type} sender='{senderName}' textLen={text.Length} '{preview}'");

        if (!Configuration.EnabledChannels.Contains(type))
        {
            Log.Debug($"[dudu的書] skip: channel {type} not enabled");
            return;
        }

        if (Configuration.IgnoreOwnMessages && IsLocalPlayer(senderName))
        {
            Log.Debug($"[dudu的書] skip: own message from '{senderName}'");
            return;
        }

        var detected = LanguageDetector.Detect(text);
        Log.Debug($"[dudu的書] detected={detected} for '{preview}'");

        if (!Configuration.EnabledSourceLanguages.Contains(detected))
        {
            Log.Debug(
                $"[dudu的書] skip: source language '{detected}' not in enabled set " +
                $"[{string.Join(",", Configuration.EnabledSourceLanguages)}]");
            return;
        }

        var target = Configuration.TargetLanguage;
        if (string.Equals(detected, target, StringComparison.OrdinalIgnoreCase))
        {
            Log.Debug($"[dudu的書] skip: detected==target ({detected})");
            return;
        }

        // Romanize whichever side is Japanese / Chinese, so the user always gets a
        // pronounceable form regardless of which direction we're translating.
        var wantSourceTranslit = Configuration.ShowTransliteration &&
                                 LanguageDetector.NeedsTransliteration(detected);
        var wantTargetTranslit = Configuration.ShowTransliteration &&
                                 !wantSourceTranslit &&
                                 LanguageDetector.NeedsTransliteration(target);

        // Capture by value so the closure doesn't see further chat traffic.
        var capturedText = text;
        var capturedSender = senderName;
        var capturedType = type;
        var capturedTarget = target;
        var ct = cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await translator.TranslateAsync(
                    capturedText, detected, capturedTarget, wantSourceTranslit, ct).ConfigureAwait(false);
                if (result == null)
                {
                    Log.Debug($"[dudu的書] no result. detected={detected} target={capturedTarget} text={capturedText}");
                    return;
                }

                var translit = result.Transliteration;
                if (wantTargetTranslit && !string.IsNullOrWhiteSpace(result.Translated))
                {
                    translit = await translator.RomanizeAsync(
                        result.Translated, capturedTarget, ct).ConfigureAwait(false) ?? string.Empty;
                }

                Log.Debug(
                    $"[dudu的書] detected={detected} target={capturedTarget} " +
                    $"trans='{result.Translated}' translit='{translit}'");

                var finalResult = new TranslationResult(result.Translated, translit ?? string.Empty, result.DetectedSource);
                var showTranslit = !string.IsNullOrWhiteSpace(translit);

                await Framework.RunOnFrameworkThread(() =>
                    PrintTranslation(capturedType, capturedSender, finalResult, showTranslit, detected));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Debug($"[dudu的書] Translator pipeline error: {ex.Message}");
            }
        }, ct);
    }

    private void PrintTranslation(
        XivChatType origin, string senderName, TranslationResult result, bool wantTranslit, string sourceLang)
    {
        var body = result.Translated;
        if (wantTranslit && !string.IsNullOrWhiteSpace(result.Transliteration))
            body = $"{body} ({result.Transliteration})";
        if (string.IsNullOrWhiteSpace(body))
            return;

        var langTag = LanguageTag(sourceLang);

        if (Configuration.UseEchoChannel)
        {
            // Echo channel has no built-in sender slot, so we inline the
            // language tag and sender name into the message itself:
            //     "[EN] Napu D'catto: 測試 (Cèshì)"
            var prefix = string.IsNullOrEmpty(senderName)
                ? $"[{langTag}] "
                : $"[{langTag}] {senderName}: ";
            var line = new SeStringBuilder()
                .AddText(prefix)
                .AddText(body)
                .Build();

            ChatGui.Print(new XivChatEntry
            {
                Type = XivChatType.Echo,
                Message = line,
            });
            return;
        }

        // Source-channel path: FFXIV draws the channel marker ([CWLS3], etc.)
        // and fills the sender slot from Name. Tucking the language tag into
        // the sender slot gives "[CWLS3]<[EN] Napu D'catto> body" while
        // letting FFXIV's per-channel chat colour apply.
        var displayName = string.IsNullOrEmpty(senderName)
            ? $"[{langTag}]"
            : $"[{langTag}] {senderName}";

        var fullMessage = new SeStringBuilder()
            .AddText(body)
            .Build();

        ChatGui.Print(new XivChatEntry
        {
            Type = origin,
            Name = displayName,
            Message = fullMessage,
        });
    }

    private static string LanguageTag(string lang) => lang switch
    {
        "ja"    => "JP",
        "zh-TW" => "ZH",
        "zh-CN" => "ZH",
        "en"    => "EN",
        "id"    => "ID",
        "ko"    => "KO",
        _       => lang.ToUpperInvariant(),
    };

    private static string ExtractCleanSenderName(SeString? sender)
    {
        if (sender == null) return string.Empty;

        // Cross-world chat embeds a world-name suffix and a special glyph in
        // the SeString; the PlayerPayload carries just the bare character
        // name, which is what we want as the prefix.
        var playerPayload = sender.Payloads.OfType<PlayerPayload>().FirstOrDefault();
        if (playerPayload != null && !string.IsNullOrEmpty(playerPayload.PlayerName))
            return playerPayload.PlayerName;

        // Fallback: strip any leading non-letter glyph (e.g. the FC tag arrow,
        // party-marker icons) and any " @ World" suffix from the plain text.
        var raw = sender.TextValue?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        var atIndex = raw.IndexOf('@');
        if (atIndex > 0) raw = raw[..atIndex].TrimEnd();

        // Drop a leading run of non-letter characters (icons, glyphs).
        var i = 0;
        while (i < raw.Length && !char.IsLetter(raw[i])) i++;
        return i > 0 ? raw[i..] : raw;
    }

    private static string ExtractPlainText(SeString message)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var payload in message.Payloads)
        {
            switch (payload)
            {
                case TextPayload text:
                    sb.Append(text.Text);
                    break;
                case AutoTranslatePayload auto:
                    sb.Append(auto.Text);
                    break;
            }
        }
        return sb.ToString().Trim();
    }

    private static bool TryCopyToClipboard(string text)
    {
        try
        {
            return Win32Clipboard.SetText(text);
        }
        catch (Exception ex)
        {
            Log.Warning($"[dudu的書] clipboard copy failed: {ex.Message}");
            return false;
        }
    }

    private static bool LooksLikeOurEcho(string senderName, string text)
    {
        // Our Echo-channel output prefixes the body with "[XX] Sender: ...".
        if (StartsWithLangTag(text)) return true;

        // Our source-channel output puts "[XX] Sender" into the Name slot,
        // so the sanitised sender we extracted will start with "[XX]".
        if (StartsWithLangTag(senderName)) return true;

        return false;
    }

    private static bool StartsWithLangTag(string s)
    {
        if (string.IsNullOrEmpty(s) || s[0] != '[') return false;
        var close = s.IndexOf(']');
        if (close < 2 || close > 5) return false; // [XX] or [XYZ]
        for (var i = 1; i < close; i++)
            if (!char.IsLetter(s[i])) return false;
        return true;
    }

    private static bool IsLocalPlayer(string senderName)
    {
        var local = ObjectTable.LocalPlayer;
        if (local == null) return false;
        var localName = local.Name.TextValue;
        if (string.IsNullOrEmpty(localName) || string.IsNullOrEmpty(senderName))
            return false;
        // Exact-match (case-insensitive) so a partial-name overlap with another
        // player doesn't cause us to ignore their messages.
        return string.Equals(senderName, localName, StringComparison.OrdinalIgnoreCase);
    }
}
