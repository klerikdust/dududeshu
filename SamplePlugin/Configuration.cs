using Dalamud.Configuration;
using Dalamud.Game.Text;
using System;
using System.Collections.Generic;

namespace SamplePlugin;

[Serializable]
public class Configuration : IPluginConfiguration
{
    private static readonly HashSet<string> AllowedLanguages = new() { "en", "ja", "zh-TW" };

    public int Version { get; set; } = 1;

    public bool Enabled { get; set; } = true;
    public bool IgnoreOwnMessages { get; set; } = true;
    public bool ShowTransliteration { get; set; } = true;
    public bool AutoSendTranslatedCommands { get; set; } = true;

    public string UiLanguage { get; set; } = "en";

    private string targetLanguage = "en";
    public string TargetLanguage
    {
        get => targetLanguage;
        set => targetLanguage = AllowedLanguages.Contains(value) ? value : "en";
    }

    public HashSet<string> EnabledSourceLanguages { get; set; } = new()
    {
        "ja",
        "zh-TW",
        "en",
    };

    public HashSet<XivChatType> EnabledChannels { get; set; } = new()
    {
        XivChatType.Say,
        XivChatType.Yell,
        XivChatType.Shout,
        XivChatType.Party,
        XivChatType.Alliance,
        XivChatType.FreeCompany,
        XivChatType.TellIncoming,
        XivChatType.CrossLinkShell1,
        XivChatType.CrossLinkShell2,
    };

    public void Save()
    {
        EnabledSourceLanguages.RemoveWhere(l => !AllowedLanguages.Contains(l));
        if (!AllowedLanguages.Contains(targetLanguage))
            targetLanguage = "en";
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
