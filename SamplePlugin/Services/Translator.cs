using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace SamplePlugin.Services;

public sealed class Translator : IDisposable
{
    private readonly HttpClient http;
    private readonly Dictionary<string, CacheEntry> cache = new();
    private readonly object cacheLock = new();
    private const int MaxCacheEntries = 512;

    public Translator()
    {
        http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8),
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");
        http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
    }

    public void Dispose() => http.Dispose();

    public async Task<string?> RomanizeAsync(string text, string lang, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var key = $"rm|{lang}|{text}";
        lock (cacheLock)
        {
            if (cache.TryGetValue(key, out var hit))
                return hit.Result.Transliteration;
        }

        var sl = NormalizeForGoogle(lang);
        var url = "https://translate.googleapis.com/translate_a/single" +
                  "?client=gtx" +
                  $"&sl={Uri.EscapeDataString(sl)}" +
                  "&tl=en" +
                  "&dt=rm&ie=UTF-8&oe=UTF-8" +
                  $"&q={HttpUtility.UrlEncode(text, Encoding.UTF8)}";

        try
        {
            using var res = await http.GetAsync(url, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                DebugLog($"Romanize failed: HTTP {(int)res.StatusCode} {res.ReasonPhrase}");
                return null;
            }

            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var parsed = ParseGtxResponse(body);
            if (parsed == null)
                return null;

            lock (cacheLock)
            {
                if (cache.Count >= MaxCacheEntries)
                    cache.Clear();
                cache[key] = new CacheEntry(parsed);
            }
            return parsed.Transliteration;
        }
        catch (Exception ex)
        {
            DebugLog($"Romanize failed: {ex.Message}");
            return null;
        }
    }

    public async Task<TranslationResult?> TranslateAsync(
        string text,
        string sourceLang,
        string targetLang,
        bool wantTransliteration,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var key = $"{sourceLang}|{targetLang}|{wantTransliteration}|{text}";
        lock (cacheLock)
        {
            if (cache.TryGetValue(key, out var hit))
                return hit.Result;
        }

        var sl = NormalizeForGoogle(sourceLang);
        var tl = NormalizeForGoogle(targetLang);

        // Legacy gtx array format: result[0] = list of segments. Each segment is
        // either [translated, original, null, null, ...] for translation or
        // [null, null, src_translit] for romanization. dt=t = translation,
        // dt=rm = romanization (romaji / pinyin).
        var url = "https://translate.googleapis.com/translate_a/single" +
                  "?client=gtx" +
                  $"&sl={Uri.EscapeDataString(sl)}" +
                  $"&tl={Uri.EscapeDataString(tl)}" +
                  "&dt=t" +
                  (wantTransliteration ? "&dt=rm" : string.Empty) +
                  "&ie=UTF-8&oe=UTF-8" +
                  $"&q={HttpUtility.UrlEncode(text, Encoding.UTF8)}";

        var parsed = await TranslateWithGtxAsync(url, ct).ConfigureAwait(false) ??
                     await TranslateWithMobileAsync(text, sl, tl, ct).ConfigureAwait(false);
        if (parsed == null)
            return null;

        lock (cacheLock)
        {
            if (cache.Count >= MaxCacheEntries)
                cache.Clear();
            cache[key] = new CacheEntry(parsed);
        }
        return parsed;
    }

    private async Task<TranslationResult?> TranslateWithGtxAsync(string url, CancellationToken ct)
    {
        try
        {
            using var res = await http.GetAsync(url, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                DebugLog($"Translate gtx failed: HTTP {(int)res.StatusCode} {res.ReasonPhrase}");
                return null;
            }

            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var parsed = ParseGtxResponse(body);
            if (parsed == null && LooksLikeGoogleBlock(body))
                DebugLog("Translate gtx failed: Google returned a traffic/CAPTCHA block page");
            return parsed;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            DebugLog($"Translate gtx failed: {ex.Message}");
            return null;
        }
    }

    private async Task<TranslationResult?> TranslateWithMobileAsync(
        string text,
        string sourceLang,
        string targetLang,
        CancellationToken ct)
    {
        var url = "https://translate.google.com/m" +
                  $"?sl={Uri.EscapeDataString(sourceLang)}" +
                  $"&tl={Uri.EscapeDataString(targetLang)}" +
                  "&hl=en-US" +
                  $"&q={HttpUtility.UrlEncode(text, Encoding.UTF8)}";

        try
        {
            using var res = await http.GetAsync(url, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                DebugLog($"Translate mobile fallback failed: HTTP {(int)res.StatusCode} {res.ReasonPhrase}");
                return null;
            }

            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var translated = ParseMobileResponse(body);
            if (string.IsNullOrWhiteSpace(translated))
            {
                if (LooksLikeGoogleBlock(body))
                    DebugLog("Translate mobile fallback failed: Google returned a traffic/CAPTCHA block page");
                else
                    DebugLog("Translate mobile fallback failed: no result container found");
                return null;
            }

            var detected = string.Equals(sourceLang, "auto", StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : sourceLang;
            return new TranslationResult(translated, string.Empty, detected);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            DebugLog($"Translate mobile fallback failed: {ex.Message}");
            return null;
        }
    }

    private static TranslationResult? ParseGtxResponse(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                return null;

            var segments = root[0];
            if (segments.ValueKind != JsonValueKind.Array)
                return null;

            var translated = new StringBuilder();
            var translit = new StringBuilder();

            foreach (var seg in segments.EnumerateArray())
            {
                if (seg.ValueKind != JsonValueKind.Array || seg.GetArrayLength() < 1)
                    continue;

                var first = seg[0];

                // Translation segment: first slot is the translated text.
                if (first.ValueKind == JsonValueKind.String)
                {
                    translated.Append(first.GetString());
                    continue;
                }

                // Romanization-only segment: first slots are null and one of the
                // later slots holds the src_translit (romaji for ja, pinyin for zh).
                if (first.ValueKind != JsonValueKind.Null)
                    continue;

                for (var i = 1; i < seg.GetArrayLength(); i++)
                {
                    var slot = seg[i];
                    if (slot.ValueKind == JsonValueKind.String)
                    {
                        translit.Append(slot.GetString());
                        break;
                    }
                }
            }

            var detected = string.Empty;
            if (root.GetArrayLength() > 2 && root[2].ValueKind == JsonValueKind.String)
                detected = root[2].GetString() ?? string.Empty;

            var translatedStr = translated.ToString().Trim();
            var translitStr = translit.ToString().Trim();
            if (translatedStr.Length == 0 && translitStr.Length == 0)
                return null;

            return new TranslationResult(translatedStr, translitStr, detected);
        }
        catch (Exception ex)
        {
            DebugLog($"gtx parse failed: {ex.Message}");
            return null;
        }
    }

    private static string? ParseMobileResponse(string body)
    {
        const string marker = "class=\"result-container\"";
        var markerIndex = body.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
            return null;

        var start = body.IndexOf('>', markerIndex);
        if (start < 0)
            return null;
        start++;

        var end = body.IndexOf("</div>", start, StringComparison.OrdinalIgnoreCase);
        if (end < 0)
            return null;

        var html = body[start..end];
        var withoutTags = Regex.Replace(html, "<.*?>", string.Empty);
        return WebUtility.HtmlDecode(withoutTags).Trim();
    }

    private static bool LooksLikeGoogleBlock(string body) =>
        body.Contains("unusual traffic", StringComparison.OrdinalIgnoreCase) ||
        body.Contains("captcha-form", StringComparison.OrdinalIgnoreCase) ||
        body.Contains("enable javascript", StringComparison.OrdinalIgnoreCase);

    private static void DebugLog(string message)
    {
        try
        {
            Plugin.Log?.Debug(message);
        }
        catch
        {
            // Plugin services are unavailable in standalone build/test contexts.
        }
    }

    private static string NormalizeForGoogle(string lang) => lang switch
    {
        "zh-TW" => "zh-TW",
        "zh-CN" => "zh-CN",
        _ => lang,
    };

    private record CacheEntry(TranslationResult Result);
}

public record TranslationResult(string Translated, string Transliteration, string DetectedSource);
