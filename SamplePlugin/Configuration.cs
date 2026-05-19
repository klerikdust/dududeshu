using Dalamud.Configuration;
using Dalamud.Game.Text;
using System;
using System.Collections.Generic;

namespace SamplePlugin;

[Serializable]
public class Configuration : IPluginConfiguration
{
    private static readonly HashSet<string> AllowedSourceLanguages = new() { "en", "ja", "zh-TW" };
    private static readonly HashSet<string> AllowedTargetLanguages = new() { "en", "ja", "zh-TW", "id" };

    public int Version { get; set; } = 2;

    public bool Enabled { get; set; } = true;
    public bool IgnoreOwnMessages { get; set; } = true;
    [Obsolete("Use ShowRomaji/ShowPinyin instead.")]
    public bool ShowTransliteration { get; set; } = true;

    public bool ShowRomaji { get; set; } = true;
    public bool ShowPinyin { get; set; } = true;

    // When true, translated echoes go to XivChatType.Echo (neutral colour).
    // When false (default), they go to the source channel so FFXIV tints the
    // line with the user's own per-channel chat colour.
    public bool UseEchoChannel { get; set; } = false;

    // When true, adds a "Translate" item to the right-click context menu of
    // the Party Finder Detail window. Reads the description directly from the
    // addon and prints the translation to Echo.
    public bool TranslatePartyFinderContextMenu { get; set; } = true;

    public string UiLanguage { get; set; } = "en";

    private string targetLanguage = "en";
    public string TargetLanguage
    {
        get => targetLanguage;
        set => targetLanguage = AllowedTargetLanguages.Contains(value) ? value : "en";
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
        ShowTransliteration = ShowRomaji || ShowPinyin;
        EnabledSourceLanguages.RemoveWhere(l => !AllowedSourceLanguages.Contains(l));
        if (!AllowedTargetLanguages.Contains(targetLanguage))
            targetLanguage = "en";
        Plugin.PluginInterface.SavePluginConfig(this);
    }

    public void UpgradeIfNeeded()
    {
        if (Version >= 2)
            return;

        ShowRomaji = ShowTransliteration;
        ShowPinyin = ShowTransliteration;
        Version = 2;
        Save();
    }
}
