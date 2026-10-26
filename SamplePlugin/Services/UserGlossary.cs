using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SamplePlugin.Services;

/// <summary>
/// The user's own glossary entries, approved from collected chat lines in the
/// review window. Stored as <c>user_glossary.json</c> in the plugin config
/// folder, in the same format as <c>Data/ffxiv_jp_glossary.json</c>, so an
/// export can be pasted straight into the shipped glossary. Loaded on top of
/// the built-in glossary: an entry here replaces a built-in one with the same
/// Japanese.
/// </summary>
public sealed class UserGlossary
{
    public const string FileName = "user_glossary.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // Keep Japanese and Chinese readable in the file and in exports.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // Where /jp input splits into lines looked up one by one ("Hey! GG").
    // '?' stays part of its line, as in the glossary: "okay?" is not "okay".
    private static readonly Regex SentenceBreak = new(@"([!.。！…]+)", RegexOptions.CultureInvariant);

    private readonly object gate = new();
    private readonly string path;
    private List<UserGlossaryEntry> entries = new();
    private Dictionary<string, (string Japanese, string Lang)> toJapanese = new(StringComparer.Ordinal);
    private DateTime loadedWriteTime;

    public UserGlossary(string configDirectory)
    {
        path = Path.Combine(configDirectory, FileName);
        Load();
    }

    /// <summary>
    /// Reloads the file if it was edited outside the plugin since it was last
    /// read or written. Returns true when it was.
    /// </summary>
    public bool ReloadIfChanged()
    {
        lock (gate)
        {
            if (WriteTime() == loadedWriteTime)
                return false;
            Load();
            return true;
        }
    }

    /// <summary>
    /// The Japanese for <paramref name="text"/> when it is exactly the
    /// English, Chinese or Indonesian of one of the user's entries or one of
    /// its aliases (compared by <see cref="FfxivGlossary.ReverseKey"/>), or a
    /// run of such lines ("Hey! GG, thanks!"). Null when any part is unknown, so
    /// the caller translates the whole text normally. Only whole lines match:
    /// a word inside a sentence ("stack" in "stack on me") can mean too many
    /// things to swap blindly.
    /// </summary>
    public (string Japanese, string Lang)? ToJapanese(string text)
    {
        lock (gate)
        {
            if (toJapanese.Count == 0 || string.IsNullOrWhiteSpace(text))
                return null;

            if (toJapanese.TryGetValue(FfxivGlossary.ReverseKey(text), out var whole))
                return whole;

            var sb = new StringBuilder();
            string? lang = null;
            foreach (var part in SentenceBreak.Split(text))
            {
                if (SentenceBreak.IsMatch(part))
                {
                    // Keep the user's emphasis unless the Japanese already has its own.
                    if (sb.Length > 0 && !char.IsPunctuation(sb[^1]))
                        sb.Append(part.Contains('!') || part.Contains('！') ? "！" : part.Length > 1 ? "…" : "。");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(part))
                    continue;
                if (!toJapanese.TryGetValue(FfxivGlossary.ReverseKey(part), out var hit))
                    return null;
                sb.Append(hit.Japanese);
                lang ??= hit.Lang;
            }

            return lang == null ? null : (sb.ToString(), lang);
        }
    }

    public int Count
    {
        get { lock (gate) return entries.Count; }
    }

    public IReadOnlyList<UserGlossaryEntry> Snapshot()
    {
        lock (gate)
            return entries.Select(e => e.Clone()).ToList();
    }

    /// <summary>Adds the entry, or replaces the one with the same Japanese.</summary>
    public void Upsert(UserGlossaryEntry entry)
    {
        var key = FfxivGlossary.KeyOf(entry.Japanese);
        lock (gate)
        {
            entries.RemoveAll(e => FfxivGlossary.KeyOf(e.Japanese) == key);
            entries.Add(entry.Clone());
            Indexed();
            Save();
        }
    }

    public void Remove(string japanese)
    {
        var key = FfxivGlossary.KeyOf(japanese);
        lock (gate)
        {
            if (entries.RemoveAll(e => FfxivGlossary.KeyOf(e.Japanese) == key) > 0)
            {
                Indexed();
                Save();
            }
        }
    }

    /// <summary>The entries as glossary JSON, ready to feed to <see cref="FfxivGlossary.Parse"/>.</summary>
    public string ToJson()
    {
        lock (gate)
            return JsonSerializer.Serialize(new UserGlossaryFile { Entries = entries }, JsonOptions);
    }

    public Stream ToStream() => new MemoryStream(Encoding.UTF8.GetBytes(ToJson()));

    private void Load()
    {
        try
        {
            loadedWriteTime = WriteTime();
            if (!File.Exists(path))
            {
                entries = new();
                Indexed();
                return;
            }
            var file = JsonSerializer.Deserialize<UserGlossaryFile>(File.ReadAllText(path), JsonOptions);
            entries = file?.Entries?.Where(e => !string.IsNullOrWhiteSpace(e.Japanese) &&
                                               !string.IsNullOrWhiteSpace(e.English))
                          .ToList() ?? new();
            Indexed();
        }
        catch (Exception ex)
        {
            // Keep the broken file for the user to fix; it is only overwritten
            // once they approve a new entry.
            Plugin.Log.Warning($"[dudu的書] user glossary unreadable: {ex.Message}");
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(new UserGlossaryFile { Entries = entries }, JsonOptions);
            var temp = path + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            File.Move(temp, path, true);
            loadedWriteTime = WriteTime();
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"[dudu的書] could not save user glossary: {ex.Message}");
        }
    }

    // Rebuilds the /jp lookup. A later entry wins when two share a rendering,
    // and any entry's own rendering wins over another entry's alias.
    private void Indexed()
    {
        var index = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        void Put(string? text, string japanese, string lang)
        {
            if (!string.IsNullOrWhiteSpace(text) && FfxivGlossary.ReverseKey(text) is { Length: > 0 } key)
                index[key] = (japanese, lang);
        }

        foreach (var entry in entries)
        {
            foreach (var (lang, aliases) in entry.Aliases ?? new())
                foreach (var alias in aliases)
                    Put(alias, entry.Japanese.Trim(), lang);
        }
        foreach (var entry in entries)
        {
            var japanese = entry.Japanese.Trim();
            Put(entry.English, japanese, "en");
            Put(entry.Chinese, japanese, "zh-TW");
            Put(entry.Indonesian, japanese, "id");
        }
        toJapanese = index;
    }

    private DateTime WriteTime() => File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;

    private sealed class UserGlossaryFile
    {
        [JsonPropertyName("about")]
        public string About { get; set; } =
            "Your own dudu的書 glossary entries, approved in the glossary review window. " +
            "Same format as Data/ffxiv_jp_glossary.json.";

        [JsonPropertyName("entries")]
        public List<UserGlossaryEntry> Entries { get; set; } = new();
    }
}

public sealed class UserGlossaryEntry
{
    [JsonPropertyName("ja")]
    public string Japanese { get; set; } = string.Empty;

    [JsonPropertyName("en")]
    public string English { get; set; } = string.Empty;

    [JsonPropertyName("zh-TW")]
    public string? Chinese { get; set; }

    [JsonPropertyName("id")]
    public string? Indonesian { get; set; }

    /// <summary>
    /// Other ways people type this line, per language ("no worries" for
    /// どまです). /jp recognises them too; they are never used as a translation.
    /// </summary>
    [JsonPropertyName("aliases")]
    public Dictionary<string, List<string>>? Aliases { get; set; }

    /// <summary>"phrase" for a whole chat line; null for a term used inside sentences.</summary>
    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    [JsonPropertyName("src")]
    public string Source { get; set; } = "user";

    [JsonIgnore]
    public bool IsPhrase => Mode == "phrase";

    public UserGlossaryEntry Clone()
    {
        var copy = (UserGlossaryEntry)MemberwiseClone();
        copy.Aliases = Aliases?.ToDictionary(pair => pair.Key, pair => pair.Value.ToList());
        return copy;
    }
}
