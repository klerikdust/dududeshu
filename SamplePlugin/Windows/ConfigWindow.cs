using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace SamplePlugin.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private readonly Configuration configuration;
    private readonly Plugin plugin;

    private static readonly (string Code, string Label)[] LanguageOptions =
    {
        ("en", "English"),
        ("ja", "Japanese"),
        ("zh-TW", "Chinese (Traditional)"),
    };

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

        ImGui.TextDisabled("Smart player messages translation tool for FF14;");
        ImGui.Spacing();
        ImGui.Separator();

        var enabled = configuration.Enabled;
        if (ImGui.Checkbox("Enable translator", ref enabled))
        {
            configuration.Enabled = enabled;
            configuration.Save();
        }

        var ignoreSelf = configuration.IgnoreOwnMessages;
        if (ImGui.Checkbox("Ignore my own messages", ref ignoreSelf))
        {
            configuration.IgnoreOwnMessages = ignoreSelf;
            configuration.Save();
        }

        var translit = configuration.ShowTransliteration;
        if (ImGui.Checkbox("Show romaji / pinyin in parentheses", ref translit))
        {
            configuration.ShowTransliteration = translit;
            configuration.Save();
        }

        var autoSend = configuration.AutoSendTranslatedCommands;
        if (ImGui.Checkbox("/jp and /zh auto-send the translation", ref autoSend))
        {
            configuration.AutoSendTranslatedCommands = autoSend;
            configuration.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(
                "On: the translated text is sent on the active channel automatically.\n" +
                "Off: the translation is copied to your clipboard and previewed in chat,\n" +
                "so you can paste it yourself with Ctrl+V before pressing Enter.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted("Translate into");
        DrawTargetCombo();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted("Translate messages written in");
        DrawSourceLanguages();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted("Channels");
        DrawChannels();
    }

    private void DrawTargetCombo()
    {
        var current = LanguageOptions.FirstOrDefault(l => l.Code == configuration.TargetLanguage);
        var label = current.Code != null ? current.Label : configuration.TargetLanguage;

        if (ImGui.BeginCombo("##targetLang", label))
        {
            foreach (var (code, name) in LanguageOptions)
            {
                var selected = configuration.TargetLanguage == code;
                if (ImGui.Selectable(name, selected))
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
        foreach (var (code, name) in LanguageOptions)
        {
            var on = configuration.EnabledSourceLanguages.Contains(code);
            if (ImGui.Checkbox($"{name}##src_{code}", ref on))
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
