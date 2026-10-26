using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using SamplePlugin.Services;

namespace SamplePlugin.Windows;

/// <summary>
/// Turns collected Japanese chat lines into the user's own glossary entries.
/// The left pane lists collected lines (or the user's entries); the right pane
/// edits one entry's en / zh-TW / id renderings.
/// </summary>
public sealed class GlossaryReviewWindow : Window, IDisposable
{
    private const int MaxFieldLength = 256;
    private const int MaxAliasesLength = 512;
    private const char AliasSeparator = '|';
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);

    private readonly Plugin plugin;
    private readonly Configuration configuration;

    private IReadOnlyList<CollectedLine> collected = Array.Empty<CollectedLine>();
    private IReadOnlyList<UserGlossaryEntry> mine = Array.Empty<UserGlossaryEntry>();
    private DateTime refreshedAt = DateTime.MinValue;
    private bool showAll;

    // The entry being edited. originalJapanese is what it was selected as, so
    // an edit to the Japanese replaces that entry instead of adding a second.
    private string? originalJapanese;
    private bool editingMine;
    private string japanese = string.Empty;
    private string english = string.Empty;
    private string chinese = string.Empty;
    private string indonesian = string.Empty;
    // Other wordings /jp should also recognise, '|'-separated as typed.
    private string englishAliases = string.Empty;
    private string chineseAliases = string.Empty;
    private string indonesianAliases = string.Empty;
    private bool phrase = true;
    private string hint = string.Empty;
    private string status = string.Empty;
    private bool statusIsError;
    private int editVersion;
    private volatile bool filling;

    public GlossaryReviewWindow(Plugin plugin)
        : base("dudu的書 · Glossary##DuduGlossaryReview")
    {
        this.plugin = plugin;
        configuration = plugin.Configuration;
        Size = new Vector2(700, 480);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(560, 360) };
        AllowPinning = true;
        AllowBackgroundBlur = true;

        TitleBarButtons.Add(new TitleBarButton
        {
            Icon = FontAwesomeIcon.Cog,
            IconOffset = new Vector2(1.5f, 1f),
            Click = _ => plugin.ToggleConfigUi(),
            ShowTooltip = () => ImGui.SetTooltip(T("titlebar.settings")),
        });
    }

    public void Dispose() { }

    private string T(string key) => Localization.Get(configuration.UiLanguage, key);

    public override void OnOpen() => refreshedAt = DateTime.MinValue;

    public override void Draw()
    {
        if (DateTime.UtcNow - refreshedAt > RefreshInterval)
            Refresh();

        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey))
            ImGui.TextWrapped(T("review.intro"));
        if (!configuration.CollectGlossaryCandidates)
            DrawCollectionOffNotice();
        ImGuiHelpers.ScaledDummy(2f);

        var footer = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y * 2;
        using (var tabs = ImRaii.TabBar("##reviewTabs"))
        {
            if (tabs)
            {
                using (var tab = ImRaii.TabItem($"{T("review.tab.collected")}###tabCollected"))
                {
                    if (tab)
                    {
                        if (editingMine)
                            Deselect();
                        DrawCollected(footer);
                    }
                }

                using (var tab = ImRaii.TabItem($"{string.Format(T("review.tab.mine"), mine.Count)}###tabMine"))
                {
                    if (tab)
                    {
                        if (!editingMine && originalJapanese != null)
                            Deselect();
                        DrawMine(footer);
                    }
                }
            }
        }

        DrawFooter();
    }

    private void Refresh()
    {
        plugin.RefreshUserGlossary();
        collected = plugin.Collector.Snapshot();
        mine = plugin.UserGlossary.Snapshot();
        refreshedAt = DateTime.UtcNow;
    }

    private void DrawCollectionOffNotice()
    {
        ConfigWindow.IconText(FontAwesomeIcon.ExclamationTriangle, T("review.off"), ImGuiColors.WarningForeground);
        ImGui.SameLine();
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.PowerOff, T("review.turnOn")))
        {
            configuration.CollectGlossaryCandidates = true;
            configuration.Save();
        }
    }

    private void DrawCollected(float footer)
    {
        var recurring = collected.Count(l => l.IsRecurring);
        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey))
            ImGui.TextUnformatted(string.Format(T("review.stats"), collected.Count, recurring));
        ImGui.SameLine();
        ImGuiComponents.ToggleButton("##showAll", ref showAll);
        ImGui.SameLine();
        ImGui.TextUnformatted(T("review.showAll"));
        if (ImGui.IsItemClicked())
            showAll = !showAll;

        var visible = showAll ? collected : collected.Where(l => l.IsRecurring).ToList();

        using (var list = ImRaii.Child("##collectedList", new Vector2(ListWidth(), -footer), true))
        {
            if (list)
            {
                if (visible.Count == 0)
                    EmptyHint(FontAwesomeIcon.Inbox, T("review.empty"));

                for (var i = 0; i < visible.Count; i++)
                {
                    var line = visible[i];
                    var selected = !editingMine && originalJapanese == line.Japanese;
                    var badge = $"×{line.Count}";
                    var right = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
                    if (ImGui.Selectable($"{line.Japanese}##c{i}", selected))
                        Select(line);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(string.Format(T("review.seen"), line.Count, line.Senders.Count));

                    // Lines several players use are the ones worth turning into entries.
                    ImGui.SameLine(right - ImGui.CalcTextSize(badge).X);
                    ImGui.TextColored(line.IsRecurring ? ImGuiColors.ParsedGold : ImGuiColors.DalamudGrey3, badge);
                }
            }
        }

        ImGui.SameLine();
        DrawEditor(footer);
    }

    private void DrawMine(float footer)
    {
        using (var list = ImRaii.Child("##mineList", new Vector2(ListWidth(), -footer), true))
        {
            if (list)
            {
                if (mine.Count == 0)
                    EmptyHint(FontAwesomeIcon.Book, T("review.mineEmpty"));

                for (var i = 0; i < mine.Count; i++)
                {
                    var entry = mine[i];
                    var selected = editingMine && originalJapanese == entry.Japanese;
                    if (ImGui.Selectable($"{entry.Japanese}##m{i}", selected))
                        Select(entry);
                    ImGui.SameLine();
                    ImGui.TextColored(ImGuiColors.DalamudGrey, $"→ {entry.English}");
                }
            }
        }

        ImGui.SameLine();
        DrawEditor(footer);
    }

    private void DrawEditor(float footer)
    {
        using var editor = ImRaii.Child("##editor", new Vector2(0, -footer), true);
        if (!editor)
            return;

        if (originalJapanese == null)
            EmptyHint(FontAwesomeIcon.HandPointLeft, T("review.pick"));
        else
            DrawFields();
    }

    private static void EmptyHint(FontAwesomeIcon icon, string text)
    {
        ImGuiHelpers.ScaledDummy(4f);
        ConfigWindow.IconText(icon, text, ImGuiColors.DalamudGrey3);
    }

    private void DrawFields()
    {
        if (!string.IsNullOrEmpty(hint))
        {
            using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey))
                ImGui.TextWrapped(hint);
            ImGuiHelpers.ScaledDummy(2f);
        }

        using (var table = ImRaii.Table("##fields", 2, ImGuiTableFlags.SizingFixedFit))
        {
            if (table)
            {
                ImGui.TableSetupColumn("label", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("value", ImGuiTableColumnFlags.WidthStretch);
                Field(T("review.japanese"), "##ja", ref japanese);
                Field(T("lang.en"), "##en", ref english);
                AliasField("##enAliases", ref englishAliases);
                Field(T("lang.zh-TW"), "##zh", ref chinese);
                AliasField("##zhAliases", ref chineseAliases);
                Field(T("lang.id"), "##id", ref indonesian);
                AliasField("##idAliases", ref indonesianAliases);
            }
        }

        ImGuiHelpers.ScaledDummy(2f);
        ImGuiComponents.ToggleButton("##phrase", ref phrase);
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(T("review.phrase"));
        if (ImGui.IsItemClicked())
            phrase = !phrase;
        ImGui.SameLine();
        ImGuiComponents.HelpMarker(T("tooltip.phrase"));

        ImGuiHelpers.ScaledDummy(2f);
        using (ImRaii.Disabled(filling))
        {
            if (ImGuiComponents.IconButtonWithText(filling ? FontAwesomeIcon.Spinner : FontAwesomeIcon.Language,
                    filling ? T("review.filling") : T("review.fill")))
                FillWithGoogle();
        }

        ImGuiHelpers.ScaledDummy(4f);
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(4f);

        var ok = ImGuiColors.SuccessBackground;
        if (ImGuiComponents.IconButtonWithText(editingMine ? FontAwesomeIcon.Save : FontAwesomeIcon.Plus,
                editingMine ? T("review.save") : T("review.add"),
                ok, Opaque(ok, 0.9f), Opaque(ok, 0.7f)))
            Commit();
        ImGui.SameLine();
        if (editingMine)
        {
            var danger = ImGuiColors.ErrorBackground;
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Trash, T("review.delete"),
                    danger, Opaque(danger, 0.9f), Opaque(danger, 0.7f)))
                DeleteMine();
        }
        else
        {
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.EyeSlash, T("review.dismiss")))
                DismissCollected();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(T("tooltip.dismiss"));
        }
    }

    private static void Field(string label, string id, ref string value)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText(id, ref value, MaxFieldLength);
    }

    private void AliasField(string id, ref string value)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudGrey3, T("review.aliases"));
        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint(id, T("review.aliasesHint"), ref value, MaxAliasesLength);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(T("tooltip.aliases"));
    }

    /// <summary>The semantic backgrounds are translucent; buttons need more body on hover/press.</summary>
    private static Vector4 Opaque(Vector4 color, float alpha) => color with { W = MathF.Max(color.W, alpha) };

    private void DrawFooter()
    {
        ImGuiHelpers.ScaledDummy(1f);

        // Clearing throws away what may be weeks of collecting, so it takes Ctrl.
        using (ImRaii.Disabled(collected.Count == 0))
        {
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Trash, T("review.clear"))
                && ImGui.GetIO().KeyCtrl)
            {
                plugin.Collector.Clear();
                if (!editingMine)
                    Deselect();
                Refresh();
            }
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(T("tooltip.clear"));

        ImGui.SameLine();
        using (ImRaii.Disabled(mine.Count == 0))
        {
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Copy, T("review.export")))
            {
                // The clipboard can be held by another process for a moment.
                var json = plugin.UserGlossary.ToJson();
                var copied = T("review.exported");
                var failed = T("review.exportFailed");
                _ = Task.Run(() =>
                {
                    var success = Win32Clipboard.SetText(json);
                    SetStatus(success ? copied : failed, !success);
                });
            }
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(T("tooltip.export"));

        if (!string.IsNullOrEmpty(status))
        {
            ImGui.SameLine();
            ConfigWindow.IconText(statusIsError ? FontAwesomeIcon.ExclamationCircle : FontAwesomeIcon.CheckCircle,
                                  status, statusIsError ? ImGuiColors.ErrorForeground : ImGuiColors.SuccessForeground);
        }
    }

    private void SetStatus(string text, bool isError)
    {
        statusIsError = isError;
        status = text;
    }

    private void Select(CollectedLine line)
    {
        Begin(line.Japanese, false);
        japanese = line.Japanese;
        phrase = true;
        hint = string.Format(T("review.seen"), line.Count, line.Senders.Count);

        // Google's reading is a starting point, put where it belongs.
        switch (line.TranslationLang)
        {
            case "en": english = line.Translation; break;
            case "zh-TW": chinese = line.Translation; break;
            case "id": indonesian = line.Translation; break;
        }
        if (!string.IsNullOrWhiteSpace(line.Translation))
            hint += $"\n{string.Format(T("review.google"), line.TranslationLang)}{line.Translation}";
    }

    private void Select(UserGlossaryEntry entry)
    {
        Begin(entry.Japanese, true);
        japanese = entry.Japanese;
        english = entry.English;
        chinese = entry.Chinese ?? string.Empty;
        indonesian = entry.Indonesian ?? string.Empty;
        englishAliases = JoinAliases(entry, "en");
        chineseAliases = JoinAliases(entry, "zh-TW");
        indonesianAliases = JoinAliases(entry, "id");
        phrase = entry.IsPhrase;
    }

    private void Begin(string original, bool isMine)
    {
        editVersion++;
        originalJapanese = original;
        editingMine = isMine;
        english = chinese = indonesian = hint = status = string.Empty;
        englishAliases = chineseAliases = indonesianAliases = string.Empty;
    }

    private void Deselect()
    {
        editVersion++;
        originalJapanese = null;
        editingMine = false;
        japanese = english = chinese = indonesian = hint = status = string.Empty;
        englishAliases = chineseAliases = indonesianAliases = string.Empty;
    }

    private static string JoinAliases(UserGlossaryEntry entry, string lang) =>
        entry.Aliases?.GetValueOrDefault(lang) is { Count: > 0 } list
            ? string.Join($" {AliasSeparator} ", list)
            : string.Empty;

    /// <summary>
    /// The typed aliases per language, minus blanks, repeats and the entry's
    /// own rendering. Null when there are none, so the file stays as before.
    /// </summary>
    private Dictionary<string, List<string>>? ParseAliases()
    {
        var aliases = new Dictionary<string, List<string>>();
        foreach (var (lang, typed, rendering) in new[]
                 {
                     ("en", englishAliases, english),
                     ("zh-TW", chineseAliases, chinese),
                     ("id", indonesianAliases, indonesian),
                 })
        {
            var own = FfxivGlossary.ReverseKey(rendering);
            var list = typed.Split(AliasSeparator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                            .Where(a => FfxivGlossary.ReverseKey(a) is { Length: > 0 } key && key != own)
                            .DistinctBy(FfxivGlossary.ReverseKey)
                            .ToList();
            if (list.Count > 0)
                aliases[lang] = list;
        }
        return aliases.Count > 0 ? aliases : null;
    }

    private void Commit()
    {
        if (string.IsNullOrWhiteSpace(japanese) || string.IsNullOrWhiteSpace(english))
        {
            SetStatus(T("review.needEnglish"), true);
            return;
        }

        var entry = new UserGlossaryEntry
        {
            Japanese = japanese.Trim(),
            English = english.Trim(),
            Chinese = NullIfBlank(chinese),
            Indonesian = NullIfBlank(indonesian),
            Aliases = ParseAliases(),
            Mode = phrase ? "phrase" : null,
        };

        if (originalJapanese != null &&
            FfxivGlossary.KeyOf(originalJapanese) != FfxivGlossary.KeyOf(entry.Japanese))
        {
            if (editingMine)
                plugin.UserGlossary.Remove(originalJapanese);
            else
                plugin.Collector.Remove(originalJapanese);
        }

        plugin.UserGlossary.Upsert(entry);
        plugin.ReloadGlossary();
        Deselect();
        SetStatus(T("review.added"), false);
        Refresh();
    }

    private void DeleteMine()
    {
        if (originalJapanese == null)
            return;
        plugin.UserGlossary.Remove(originalJapanese);
        plugin.ReloadGlossary();
        Deselect();
        Refresh();
    }

    private void DismissCollected()
    {
        if (originalJapanese == null)
            return;
        plugin.Collector.Dismiss(originalJapanese);
        Deselect();
        Refresh();
    }

    private void FillWithGoogle()
    {
        var text = japanese.Trim();
        if (text.Length == 0 || filling)
            return;

        var wanted = new List<string>();
        if (string.IsNullOrWhiteSpace(english)) wanted.Add("en");
        if (string.IsNullOrWhiteSpace(chinese)) wanted.Add("zh-TW");
        if (string.IsNullOrWhiteSpace(indonesian)) wanted.Add("id");
        if (wanted.Count == 0)
            return;

        filling = true;
        var version = editVersion;
        var ct = plugin.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var lang in wanted)
                {
                    var result = await plugin.Translator.TranslateAsync(text, "ja", lang, false, ct)
                                     .ConfigureAwait(false);
                    var translated = result?.Translated;
                    if (string.IsNullOrWhiteSpace(translated))
                        continue;

                    await Plugin.Framework.RunOnFrameworkThread(() =>
                    {
                        // The user may have moved on to another line meanwhile.
                        if (version != editVersion)
                            return;
                        switch (lang)
                        {
                            case "en" when string.IsNullOrWhiteSpace(english): english = translated; break;
                            case "zh-TW" when string.IsNullOrWhiteSpace(chinese): chinese = translated; break;
                            case "id" when string.IsNullOrWhiteSpace(indonesian): indonesian = translated; break;
                        }
                    }).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Plugin.Log.Debug($"[dudu的書] glossary review fill failed: {ex.Message}");
            }
            finally
            {
                filling = false;
            }
        }, ct);
    }

    private static string? NullIfBlank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static float ListWidth() => 240f * ImGuiHelpers.GlobalScale;
}
