using System;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace SamplePlugin.Services;

public sealed class PartyFinderContextMenu : IDisposable
{
    private const string AddonName = "LookingForGroupDetail";

    private readonly Configuration configuration;
    private readonly Translator translator;
    private readonly IContextMenu contextMenu;
    private readonly IFramework framework;
    private readonly IChatGui chatGui;
    private readonly IPluginLog log;
    private readonly CancellationToken pluginToken;

    public PartyFinderContextMenu(
        Configuration configuration,
        Translator translator,
        IContextMenu contextMenu,
        IFramework framework,
        IChatGui chatGui,
        IPluginLog log,
        CancellationToken pluginToken)
    {
        this.configuration = configuration;
        this.translator = translator;
        this.contextMenu = contextMenu;
        this.framework = framework;
        this.chatGui = chatGui;
        this.log = log;
        this.pluginToken = pluginToken;

        contextMenu.OnMenuOpened += OnMenuOpened;
    }

    public void Dispose()
    {
        contextMenu.OnMenuOpened -= OnMenuOpened;
    }

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (!configuration.Enabled || !configuration.TranslatePartyFinderContextMenu)
            return;
        if (args.AddonName != AddonName)
            return;

        // The MenuItem is rebuilt every open so the label tracks the current
        // UiLanguage setting without us having to refresh a cached instance.
        args.AddMenuItem(new MenuItem
        {
            UseDefaultPrefix = true,
            Name = Localization.Get(configuration.UiLanguage, "pfMenu.translate"),
            OnClicked = OnTranslateClicked,
        });
    }

    private void OnTranslateClicked(IMenuItemClickedArgs args)
    {
        var addonPtr = args.AddonPtr;
        if (addonPtr == IntPtr.Zero) return;

        var description = ReadDescription(addonPtr);
        if (string.IsNullOrWhiteSpace(description)) return;

        // Strip SeString control bytes (0x02..0x03 framing for icons/role
        // glyphs in PF descriptions) so the translator gets clean text. We
        // collapse them to spaces rather than dropping outright so adjacent
        // tokens don't get glued together.
        description = StripControlBytes(description);
        if (string.IsNullOrWhiteSpace(description)) return;

        var target = configuration.TargetLanguage;
        var detected = LanguageDetector.Detect(description);
        if (detected == "und") detected = "auto";

        var captured = description;
        var capturedTarget = target;
        var capturedDetected = detected;
        var ct = pluginToken;

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await translator.TranslateAsync(
                    captured, capturedDetected, capturedTarget, false, ct).ConfigureAwait(false);
                var translated = result?.Translated;
                if (string.IsNullOrWhiteSpace(translated)) return;

                await framework.RunOnFrameworkThread(
                    () => PrintPfTranslation(translated!, capturedTarget));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                log.Debug($"[dudu的書] PF menu pipeline error: {ex.Message}");
            }
        }, ct);
    }

    private unsafe string ReadDescription(IntPtr addonPtr)
    {
        try
        {
            var pfAddon = (AddonLookingForGroupDetail*)addonPtr;
            return pfAddon->DescriptionString.ToString();
        }
        catch (Exception ex)
        {
            log.Warning($"[dudu的書] PF menu: failed to read description: {ex.Message}");
            return string.Empty;
        }
    }

    private void PrintPfTranslation(string translated, string targetLang)
    {
        var langTag = LanguageTag(targetLang);
        var senderLabel = Localization.Get(configuration.UiLanguage, "pf.sender");
        var prefix = $"[{langTag}] {senderLabel}: ";

        var line = new SeStringBuilder()
            .AddText(prefix)
            .AddText(translated)
            .Build();

        chatGui.Print(new XivChatEntry
        {
            Type = XivChatType.Echo,
            Message = line,
        });
    }

    private static string StripControlBytes(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var sb = new System.Text.StringBuilder(s.Length);
        var lastWasSpace = false;
        foreach (var c in s)
        {
            if (c < 0x20 && c != '\n' && c != '\r' && c != '\t')
            {
                if (!lastWasSpace) { sb.Append(' '); lastWasSpace = true; }
            }
            else
            {
                sb.Append(c);
                lastWasSpace = c == ' ';
            }
        }
        return sb.ToString().Trim();
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
}
