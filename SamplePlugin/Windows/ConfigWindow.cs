using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using SamplePlugin.Services;

namespace SamplePlugin.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private readonly Configuration configuration;
    private readonly Plugin plugin;

    private static readonly string[] SourceLanguageCodes = { "en", "ja", "zh-TW", "id" };
    private static readonly string[] TargetLanguageCodes = { "en", "ja", "zh-TW", "id" };

    private static readonly (XivChatType Type, string Label)[] ChannelOptions =
    {
        (XivChatType.Say, "Say"),
        (XivChatType.Yell, "Yell"),
        (XivChatType.Shout, "Shout"),
        (XivChatType.Party, "Party"),
        (XivChatType.Alliance, "Alliance"),
        (XivChatType.FreeCompany, "Free Company"),
        (XivChatType.TellIncoming, "Tell (incoming)"),
        (XivChatType.CrossLinkShell1, "CWLS 1"),
        (XivChatType.CrossLinkShell2, "CWLS 2"),
        (XivChatType.CrossLinkShell3, "CWLS 3"),
        (XivChatType.CrossLinkShell4, "CWLS 4"),
        (XivChatType.CrossLinkShell5, "CWLS 5"),
        (XivChatType.CrossLinkShell6, "CWLS 6"),
        (XivChatType.CrossLinkShell7, "CWLS 7"),
        (XivChatType.CrossLinkShell8, "CWLS 8"),
        (XivChatType.Ls1, "LS 1"),
        (XivChatType.Ls2, "LS 2"),
        (XivChatType.Ls3, "LS 3"),
        (XivChatType.Ls4, "LS 4"),
        (XivChatType.Ls5, "LS 5"),
        (XivChatType.Ls6, "LS 6"),
        (XivChatType.Ls7, "LS 7"),
        (XivChatType.Ls8, "LS 8"),
        (XivChatType.NoviceNetwork, "Novice Network"),
        (XivChatType.PvPTeam, "PvP Team"),
    };

    public ConfigWindow(Plugin plugin)
        : base("dudu的書##DuduBookConfig", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        Size = new Vector2(420, 640);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(360, 460) };
        AllowPinning = true;
        AllowBackgroundBlur = true;
        configuration = plugin.Configuration;
        this.plugin = plugin;

        TitleBarButtons.Add(new TitleBarButton
        {
            Icon = FontAwesomeIcon.BookOpen,
            IconOffset = new Vector2(1.5f, 1f),
            Click = _ => plugin.ToggleReviewUi(),
            ShowTooltip = () => ImGui.SetTooltip(string.Format(T("button.review"), plugin.Collector.Count)),
        });
    }

    public void Dispose() { }

    private string T(string key) => Localization.Get(configuration.UiLanguage, key);

    public override void Draw()
    {
        DrawHeaderImage();
        DrawStatusCard();
        ImGuiHelpers.ScaledDummy(4f);

        // Tab ids after ### stay fixed so switching the UI language keeps the selected tab.
        using var tabs = ImRaii.TabBar("##duduTabs");
        if (!tabs)
            return;

        DrawTab($"{T("tab.general")}###tabGeneral", DrawGeneralTab);
        DrawTab($"{T("tab.languages")}###tabLanguages", DrawLanguagesTab);
        DrawTab($"{T("section.channels")}###tabChannels", DrawChannelsTab);
        DrawTab($"{T("tab.glossary")}###tabGlossary", DrawGlossaryTab);
    }

    private static void DrawTab(string label, Action draw)
    {
        using var tab = ImRaii.TabItem(label);
        if (!tab)
            return;

        // Only the tab body scrolls; the header and switch stay in view.
        using var body = ImRaii.Child("##tabBody", Vector2.Zero, false);
        if (body)
            draw();
    }

    private void DrawHeaderImage()
    {
        var path = plugin.DuduImagePath;
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
            return;

        var tex = Plugin.TextureProvider.GetFromFile(path).GetWrapOrDefault();
        if (tex == null)
            return;

        var avail = ImGui.GetContentRegionAvail().X;
        var maxHeight = 96f * ImGuiHelpers.GlobalScale;
        var aspect = tex.Size.Y / tex.Size.X;
        var width = MathF.Min(avail, tex.Size.X);
        var height = width * aspect;
        if (height > maxHeight)
        {
            height = maxHeight;
            width = height / aspect;
        }

        ImGuiHelpers.CenterCursorFor(width);
        ImGui.Image(tex.Handle, new Vector2(width, height));

        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey))
            ImGuiHelpers.CenteredText(T("tagline"));
        ImGuiHelpers.ScaledDummy(2f);
    }

    /// <summary>The master switch, tinted by state so "is it on?" reads at a glance.</summary>
    private void DrawStatusCard()
    {
        var enabled = configuration.Enabled;
        var style = ImGui.GetStyle();
        var height = ImGui.GetFrameHeight() + style.WindowPadding.Y * 2;

        using var bg = ImRaii.PushColor(ImGuiCol.ChildBg, enabled ? ImGuiColors.SuccessBackground : ImGuiColors.WarningBackground);
        using var rounding = ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, style.FrameRounding + 2f * ImGuiHelpers.GlobalScale);
        using var card = ImRaii.Child("##statusCard", new Vector2(-1, height), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysUseWindowPadding);
        if (!card)
            return;

        if (Toggle("##enabled", T("enable.translator"), ref enabled))
        {
            configuration.Enabled = enabled;
            configuration.Save();
        }

        var status = enabled ? T("status.on") : T("status.off");
        var icon = enabled ? FontAwesomeIcon.CheckCircle : FontAwesomeIcon.PauseCircle;
        var color = enabled ? ImGuiColors.SuccessForeground : ImGuiColors.WarningForeground;
        var width = IconTextWidth(icon, status);
        ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - width);
        IconText(icon, status, color);
    }

    private void DrawGeneralTab()
    {
        SectionHeader(FontAwesomeIcon.Globe, T("section.uiLanguage"));
        DrawUiLanguageCombo();

        SectionHeader(FontAwesomeIcon.Comments, T("section.chat"));
        Setting("##ignoreOwn", "enable.ignoreOwn", null,
                () => configuration.IgnoreOwnMessages, v => configuration.IgnoreOwnMessages = v);
        Setting("##emotes", "enable.translateEmotes", "tooltip.translateEmotes",
                () => configuration.TranslateEmoteMessages, v => configuration.TranslateEmoteMessages = v);
        Setting("##echo", "enable.useEcho", "tooltip.useEcho",
                () => configuration.UseEchoChannel, v => configuration.UseEchoChannel = v);
        Setting("##pfMenu", "enable.translatePfMenu", "tooltip.translatePfMenu",
                () => configuration.TranslatePartyFinderContextMenu, v => configuration.TranslatePartyFinderContextMenu = v);
    }

    private void DrawLanguagesTab()
    {
        SectionHeader(FontAwesomeIcon.ArrowRight, T("section.translateInto"));
        DrawTargetCombo();

        SectionHeader(FontAwesomeIcon.Inbox, T("section.translateFrom"));
        DrawSourceLanguages();

        SectionHeader(FontAwesomeIcon.Font, T("section.reading"));
        Setting("##romaji", "enable.romaji", null,
                () => configuration.ShowRomaji, v => configuration.ShowRomaji = v);
        Setting("##pinyin", "enable.pinyin", null,
                () => configuration.ShowPinyin, v => configuration.ShowPinyin = v);
    }

    private void DrawChannelsTab()
    {
        ImGuiHelpers.ScaledDummy(2f);
        var enabledCount = ChannelOptions.Count(c => configuration.EnabledChannels.Contains(c.Type));
        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey))
            ImGui.TextUnformatted(string.Format(T("channels.count"), enabledCount, ChannelOptions.Length));

        var allLabel = T("channels.all");
        var noneLabel = T("channels.none");
        var buttonsWidth = ImGuiComponents.GetIconButtonWithTextWidth(FontAwesomeIcon.CheckDouble, allLabel)
                           + ImGuiComponents.GetIconButtonWithTextWidth(FontAwesomeIcon.Times, noneLabel)
                           + ImGui.GetStyle().ItemSpacing.X;
        ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - buttonsWidth);
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.CheckDouble, allLabel))
        {
            foreach (var (type, _) in ChannelOptions)
                configuration.EnabledChannels.Add(type);
            configuration.Save();
        }
        ImGui.SameLine();
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Times, noneLabel))
        {
            foreach (var (type, _) in ChannelOptions)
                configuration.EnabledChannels.Remove(type);
            configuration.Save();
        }

        ImGuiHelpers.ScaledDummy(2f);
        using var table = ImRaii.Table("##channels", 2, ImGuiTableFlags.BordersOuter | ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX);
        if (!table)
            return;

        foreach (var (type, name) in ChannelOptions)
        {
            ImGui.TableNextColumn();
            var on = configuration.EnabledChannels.Contains(type);
            if (ImGui.Checkbox($"{name}##chan_{(int)type}", ref on))
            {
                if (on) configuration.EnabledChannels.Add(type);
                else configuration.EnabledChannels.Remove(type);
                configuration.Save();
            }
        }
    }

    private void DrawGlossaryTab()
    {
        SectionHeader(FontAwesomeIcon.Book, T("tab.glossary"));
        Setting("##glossary", "enable.glossary", "tooltip.glossary",
                () => configuration.UseFfxivGlossary, v => configuration.UseFfxivGlossary = v);
        Setting("##collect", "enable.collect", "tooltip.collect",
                () => configuration.CollectGlossaryCandidates, v => configuration.CollectGlossaryCandidates = v);

        ImGuiHelpers.ScaledDummy(6f);
        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey))
            ImGui.TextUnformatted(string.Format(T("glossary.stats"), plugin.Collector.Count, plugin.UserGlossary.Count));
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.BookOpen,
                string.Format(T("button.review"), plugin.Collector.Count), new Vector2(-1, 0)))
            plugin.ToggleReviewUi();
    }

    private void DrawUiLanguageCombo()
    {
        var currentCode = configuration.UiLanguage;
        var currentLabel = Localization.AvailableLocales.FirstOrDefault(l => l.Code == currentCode).Label
                           ?? currentCode;

        ImGui.SetNextItemWidth(-1);
        using var combo = ImRaii.Combo("##uiLang", currentLabel);
        if (!combo)
            return;

        foreach (var (code, label) in Localization.AvailableLocales)
        {
            var selected = currentCode == code;
            if (ImGui.Selectable(label, selected))
            {
                configuration.UiLanguage = code;
                configuration.Save();
            }
            if (selected) ImGui.SetItemDefaultFocus();
        }
    }

    private void DrawTargetCombo()
    {
        var current = configuration.TargetLanguage;

        ImGui.SetNextItemWidth(-1);
        using var combo = ImRaii.Combo("##targetLang", T($"lang.{current}"));
        if (!combo)
            return;

        foreach (var code in TargetLanguageCodes)
        {
            var selected = current == code;
            if (ImGui.Selectable(T($"lang.{code}"), selected))
            {
                configuration.TargetLanguage = code;
                configuration.Save();
            }
            if (selected) ImGui.SetItemDefaultFocus();
        }
    }

    private void DrawSourceLanguages()
    {
        using var table = ImRaii.Table("##sourceLangs", 2);
        if (!table)
            return;

        foreach (var code in SourceLanguageCodes)
        {
            ImGui.TableNextColumn();
            var on = configuration.EnabledSourceLanguages.Contains(code);
            if (Toggle($"##src_{code}", T($"lang.{code}"), ref on))
            {
                if (on) configuration.EnabledSourceLanguages.Add(code);
                else configuration.EnabledSourceLanguages.Remove(code);
                configuration.Save();
            }
        }
    }

    /// <summary>A toggle row bound to a configuration flag; saves on change.</summary>
    private void Setting(string id, string labelKey, string? helpKey, Func<bool> get, Action<bool> set)
    {
        var value = get();
        if (Toggle(id, T(labelKey), ref value, helpKey == null ? null : T(helpKey)))
        {
            set(value);
            configuration.Save();
        }
    }

    /// <summary>Dalamud's switch with a label that is clickable too, plus an optional (?) marker.</summary>
    private static bool Toggle(string id, string label, ref bool value, string? help = null)
    {
        var changed = ImGuiComponents.ToggleButton(id, ref value);
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        if (ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (ImGui.IsItemClicked())
        {
            value = !value;
            changed = true;
        }

        if (help != null)
        {
            ImGui.SameLine();
            ImGuiComponents.HelpMarker(help);
        }

        return changed;
    }

    private static void SectionHeader(FontAwesomeIcon icon, string title)
    {
        ImGuiHelpers.ScaledDummy(4f);
        IconText(icon, title, ImGuiColors.DalamudViolet);
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(2f);
    }

    internal static void IconText(FontAwesomeIcon icon, string text, Vector4 color)
    {
        using var c = ImRaii.PushColor(ImGuiCol.Text, color);
        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushFont(UiBuilder.IconFont))
            ImGui.TextUnformatted(icon.ToIconString());
        ImGui.SameLine();
        ImGui.TextUnformatted(text);
    }

    internal static float IconTextWidth(FontAwesomeIcon icon, string text)
    {
        float iconWidth;
        using (ImRaii.PushFont(UiBuilder.IconFont))
            iconWidth = ImGui.CalcTextSize(icon.ToIconString()).X;
        return iconWidth + ImGui.GetStyle().ItemSpacing.X + ImGui.CalcTextSize(text).X;
    }
}
