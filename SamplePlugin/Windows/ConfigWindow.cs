using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
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
        : base("dudu的書##DuduBookConfig",
               ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        Size = new Vector2(360, 600);
        SizeCondition = ImGuiCond.FirstUseEver;
        configuration = plugin.Configuration;
        this.plugin = plugin;
    }

    public void Dispose() { }

    private string T(string key) => Localization.Get(configuration.UiLanguage, key);

    private void DrawHeaderImage()
    {
        var path = plugin.DuduImagePath;
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
            return;

        var tex = Plugin.TextureProvider.GetFromFile(path).GetWrapOrDefault();
        if (tex == null)
            return;

        var avail = ImGui.GetContentRegionAvail().X;
        var maxHeight = 120f * ImGuiHelpers.GlobalScale;
        var aspect = tex.Size.Y / tex.Size.X;
        var width = MathF.Min(avail, tex.Size.X);
        var height = width * aspect;
        if (height > maxHeight)
        {
            height = maxHeight;
            width = height / aspect;
        }

        var indent = MathF.Max(0f, (avail - width) * 0.5f);
        if (indent > 0f) ImGui.Indent(indent);
        ImGui.Image(tex.Handle, new Vector2(width, height));
        if (indent > 0f) ImGui.Unindent(indent);
        ImGui.Spacing();
    }

    public override void Draw()
    {
        DrawHeaderImage();

        ImGui.TextDisabled(T("tagline"));
        ImGui.Spacing();
        ImGui.Separator();

        ImGui.TextUnformatted(T("section.uiLanguage"));
        DrawUiLanguageCombo();

        ImGui.Spacing();
        ImGui.Separator();

        var enabled = configuration.Enabled;
        if (ImGui.Checkbox(T("enable.translator"), ref enabled))
        {
            configuration.Enabled = enabled;
            configuration.Save();
        }

        var ignoreSelf = configuration.IgnoreOwnMessages;
        if (ImGui.Checkbox(T("enable.ignoreOwn"), ref ignoreSelf))
        {
            configuration.IgnoreOwnMessages = ignoreSelf;
            configuration.Save();
        }

        var translateEmotes = configuration.TranslateEmoteMessages;
        if (ImGui.Checkbox(T("enable.translateEmotes"), ref translateEmotes))
        {
            configuration.TranslateEmoteMessages = translateEmotes;
            configuration.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(T("tooltip.translateEmotes"));

        var showRomaji = configuration.ShowRomaji;
        if (ImGui.Checkbox(T("enable.romaji"), ref showRomaji))
        {
            configuration.ShowRomaji = showRomaji;
            configuration.Save();
        }

        var showPinyin = configuration.ShowPinyin;
        if (ImGui.Checkbox(T("enable.pinyin"), ref showPinyin))
        {
            configuration.ShowPinyin = showPinyin;
            configuration.Save();
        }

        var useEcho = configuration.UseEchoChannel;
        if (ImGui.Checkbox(T("enable.useEcho"), ref useEcho))
        {
            configuration.UseEchoChannel = useEcho;
            configuration.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(T("tooltip.useEcho"));

        var translatePfMenu = configuration.TranslatePartyFinderContextMenu;
        if (ImGui.Checkbox(T("enable.translatePfMenu"), ref translatePfMenu))
        {
            configuration.TranslatePartyFinderContextMenu = translatePfMenu;
            configuration.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(T("tooltip.translatePfMenu"));

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted(T("section.translateInto"));
        DrawTargetCombo();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted(T("section.translateFrom"));
        DrawSourceLanguages();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted(T("section.channels"));
        DrawChannels();
    }

    private void DrawUiLanguageCombo()
    {
        var currentCode = configuration.UiLanguage;
        var currentLabel = Localization.AvailableLocales.FirstOrDefault(l => l.Code == currentCode).Label
                           ?? currentCode;

        if (ImGui.BeginCombo("##uiLang", currentLabel))
        {
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
            ImGui.EndCombo();
        }
    }

    private void DrawTargetCombo()
    {
        var current = configuration.TargetLanguage;
        var label = T($"lang.{current}");

        if (ImGui.BeginCombo("##targetLang", label))
        {
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
            ImGui.EndCombo();
        }
    }

    private void DrawSourceLanguages()
    {
        foreach (var code in SourceLanguageCodes)
        {
            var on = configuration.EnabledSourceLanguages.Contains(code);
            if (ImGui.Checkbox($"{T($"lang.{code}")}##src_{code}", ref on))
            {
                if (on) configuration.EnabledSourceLanguages.Add(code);
                else configuration.EnabledSourceLanguages.Remove(code);
                configuration.Save();
            }
        }
    }

    private void DrawChannels()
    {
        if (ImGui.BeginChild("##channels", new Vector2(0, 180), true))
        {
            foreach (var (type, name) in ChannelOptions)
            {
                var on = configuration.EnabledChannels.Contains(type);
                if (ImGui.Checkbox($"{name}##chan_{(int)type}", ref on))
                {
                    if (on) configuration.EnabledChannels.Add(type);
                    else configuration.EnabledChannels.Remove(type);
                    configuration.Save();
                }
            }
        }
        ImGui.EndChild();
    }
}
