using System;
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
            PluginInterface.AssemblyLocation.Directory?.FullName ?? string.Empty, "dudu的書.png");

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

        CommandManager.AddHandler(SendJaCommand, new CommandInfo(OnTranslateAndSend)
        {
            HelpMessage = "Translate the rest of the line into Japanese and send it.",
        });
        CommandManager.AddHandler(SendZhCommand, new CommandInfo(OnTranslateAndSend)
        {
            HelpMessage = "Translate the rest of the line into Traditional Chinese and send it.",
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

        try { cts.Cancel(); } catch { /* ignore */ }
        cts.Dispose();
        translator.Dispose();
    }

    public void ToggleConfigUi() => configWindow.Toggle();

    private void OnCommand(string command, string args) => ToggleConfigUi();

    private void OnTranslateAndSend(string command, string args)
    {
        var text = (args ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(text))
        {
            ChatGui.PrintError($"[dudu的書] Usage: {command} <message>");
            return;
        }

        var target = command switch
        {
            SendJaCommand => "ja",
            SendZhCommand => "zh-TW",
            _ => "ja",
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
                        ChatGui.PrintError($"[dudu的書] Translation failed for: {text}"));
                    return;
                }

                await Framework.RunOnFrameworkThread(() =>
                {
                    if (Configuration.AutoSendTranslatedCommands)
                    {
                        if (!ChatSender.Send(translated))
                            ChatGui.PrintError("[dudu的書] Could not send the translated message.");
                        return;
                    }

                    if (TryCopyToClipboard(translated))
                    {
                        var preview = new SeStringBuilder()
                            .AddUiForeground("[dudu的書] ", 52)
                            .AddText("Copied to clipboard. Paste with Ctrl+V: ")
                            .AddUiForeground(translated, 60)
                            .Build();
                        ChatGui.Print(new XivChatEntry { Type = XivChatType.Echo, Message = preview });
                    }
                    else
                    {
                        ChatGui.PrintError($"[dudu的書] Could not copy translation to clipboard: {translated}");
                    }
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Warning($"[dudu的書] /jp or /zh failed: {ex.Message}");
            }
        }, ct);
    }

    private void OnChatMessage(IHandleableChatMessage message)
    {
        if (!Configuration.Enabled)
            return;

        var type = message.LogKind;
        var senderName = message.Sender?.TextValue ?? string.Empty;
        var text = ExtractPlainText(message.Message);

        var preview = text.Length > 60 ? text[..60] + "…" : text;
        Log.Information(
            $"[dudu的書] in: type={type} sender='{senderName}' textLen={text.Length} '{preview}'");

        if (!Configuration.EnabledChannels.Contains(type))
        {
            Log.Information($"[dudu的書] skip: channel {type} not enabled");
            return;
        }

        if (Configuration.IgnoreOwnMessages && IsLocalPlayer(senderName))
        {
            Log.Information($"[dudu的書] skip: own message from '{senderName}'");
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            Log.Information("[dudu的書] skip: empty text after payload extraction");
            return;
        }

        var detected = LanguageDetector.Detect(text);
        Log.Information($"[dudu的書] detected={detected} for '{preview}'");

        if (!Configuration.EnabledSourceLanguages.Contains(detected))
        {
            Log.Information(
                $"[dudu的書] skip: source language '{detected}' not in enabled set " +
                $"[{string.Join(",", Configuration.EnabledSourceLanguages)}]");
            return;
        }

        var target = Configuration.TargetLanguage;
        if (string.Equals(detected, target, StringComparison.OrdinalIgnoreCase))
        {
            Log.Information($"[dudu的書] skip: detected==target ({detected})");
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
                    Log.Information($"[dudu的書] no result. detected={detected} target={capturedTarget} text={capturedText}");
                    return;
                }

                var translit = result.Transliteration;
                if (wantTargetTranslit && !string.IsNullOrWhiteSpace(result.Translated))
                {
                    translit = await translator.RomanizeAsync(
                        result.Translated, capturedTarget, ct).ConfigureAwait(false) ?? string.Empty;
                }

                Log.Information(
                    $"[dudu的書] detected={detected} target={capturedTarget} " +
                    $"trans='{result.Translated}' translit='{translit}'");

                var finalResult = new TranslationResult(result.Translated, translit ?? string.Empty, result.DetectedSource);
                var showTranslit = !string.IsNullOrWhiteSpace(translit);

                await Framework.RunOnFrameworkThread(() =>
                    PrintTranslation(capturedType, capturedSender, finalResult, showTranslit));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Debug($"[dudu的書] Translator pipeline error: {ex.Message}");
            }
        }, ct);
    }

    private void PrintTranslation(
        XivChatType origin, string senderName, TranslationResult result, bool wantTranslit)
    {
        var body = result.Translated;
        if (wantTranslit && !string.IsNullOrWhiteSpace(result.Transliteration))
            body = $"{body} ({result.Transliteration})";
        if (string.IsNullOrWhiteSpace(body))
            return;

        var prefix = string.IsNullOrEmpty(senderName)
            ? $"[{ChannelTag(origin)}] "
            : $"[{ChannelTag(origin)}] {senderName}: ";

        var line = new SeStringBuilder()
            .AddText(prefix)
            .AddText(body)
            .Build();

        ChatGui.Print(new XivChatEntry
        {
            Type = XivChatType.Echo,
            Message = line,
        });
    }

    private static string ChannelTag(XivChatType type) => type switch
    {
        XivChatType.Say => "Say",
        XivChatType.Yell => "Yell",
        XivChatType.Shout => "Shout",
        XivChatType.Party => "Party",
        XivChatType.Alliance => "Alliance",
        XivChatType.FreeCompany => "FC",
        XivChatType.TellIncoming => "Tell",
        XivChatType.NoviceNetwork => "NN",
        XivChatType.PvPTeam => "PvP",
        XivChatType.CrossLinkShell1 => "CWLS1",
        XivChatType.CrossLinkShell2 => "CWLS2",
        XivChatType.CrossLinkShell3 => "CWLS3",
        XivChatType.CrossLinkShell4 => "CWLS4",
        XivChatType.CrossLinkShell5 => "CWLS5",
        XivChatType.CrossLinkShell6 => "CWLS6",
        XivChatType.CrossLinkShell7 => "CWLS7",
        XivChatType.CrossLinkShell8 => "CWLS8",
        XivChatType.Ls1 => "LS1",
        XivChatType.Ls2 => "LS2",
        XivChatType.Ls3 => "LS3",
        XivChatType.Ls4 => "LS4",
        XivChatType.Ls5 => "LS5",
        XivChatType.Ls6 => "LS6",
        XivChatType.Ls7 => "LS7",
        XivChatType.Ls8 => "LS8",
        _ => type.ToString(),
    };

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

    private static bool IsLocalPlayer(string senderName)
    {
        var local = ObjectTable.LocalPlayer;
        if (local == null) return false;
        var localName = local.Name.TextValue;
        return !string.IsNullOrEmpty(localName) &&
               senderName.Contains(localName, StringComparison.OrdinalIgnoreCase);
    }
}
