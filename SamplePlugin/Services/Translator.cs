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

    // One retry only. Markup answers are usually transient (an interstitial
    // between two good responses), so a single second pass recovers most of
    // them; anything that fails twice is dropped rather than shown.
    private const int MaxAttempts = 2;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(250);

    // Variants of the same legacy array API. The quota is per client id, not
    // per IP: client=gtx can sit on HTTP 429 for hours while the other two
    // answer normally from the same machine. Rotating across them is what
    // keeps romaji/pinyin alive, because the /m fallback below returns a
    // translation only and has no transliteration to scrape at all.
    private static readonly GtxEndpoint[] GtxEndpoints =
    {
        new("gtx", "https://translate.googleapis.com/translate_a/single", "gtx"),
        new("at", "https://translate.googleapis.com/translate_a/single", "at"),
        new("dict-chrome-ex", "https://clients5.google.com/translate_a/single", "dict-chrome-ex"),
    };

    // Long enough that a throttled client stops costing us a round-trip on
    // every message, short enough that we notice when its quota refills.
    private static readonly TimeSpan ThrottleCooldown = TimeSpan.FromMinutes(10);
    private readonly Dictionary<string, DateTime> throttledUntil = new();
    private readonly object throttleLock = new();

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
        var query = $"&sl={Uri.EscapeDataString(sl)}" +
                    "&tl=en" +
                    "&dt=rm&ie=UTF-8&oe=UTF-8" +
                    $"&q={HttpUtility.UrlEncode(text, Encoding.UTF8)}";

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var (parsed, sawMarkup) = await TryGtxEndpointsAsync(
                query, true, attempt - 1, "Romanize", ct).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(parsed?.Transliteration))
            {
                lock (cacheLock)
                {
                    if (cache.Count >= MaxCacheEntries)
                        cache.Clear();
                    cache[key] = new CacheEntry(parsed);
                }
                return parsed.Transliteration;
            }

            // Only markup earns the retry. An empty answer from every endpoint
            // means this text simply has no romanization to give.
            if (!sawMarkup || attempt == MaxAttempts)
                break;

            await Task.Delay(RetryDelay, ct).ConfigureAwait(false);
        }

        return null;
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
        var query = $"&sl={Uri.EscapeDataString(sl)}" +
                    $"&tl={Uri.EscapeDataString(tl)}" +
                    "&dt=t" +
                    (wantTransliteration ? "&dt=rm" : string.Empty) +
                    "&ie=UTF-8&oe=UTF-8" +
                    $"&q={HttpUtility.UrlEncode(text, Encoding.UTF8)}";

        TranslationResult? parsed = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var (candidate, sawMarkup) = await TranslateOnceAsync(
                query, text, sl, tl, wantTransliteration, attempt - 1, ct).ConfigureAwait(false);

            if (candidate != null)
            {
                parsed = candidate;
                break;
            }

            // Only markup earns the retry. A plain transport failure has
            // already burned every endpoint, and a second pass would just
            // double the latency of an outage.
            if (!sawMarkup || attempt == MaxAttempts)
                break;

            DebugLog($"Translate attempt {attempt} returned markup; retrying");
            await Task.Delay(RetryDelay, ct).ConfigureAwait(false);
        }

        // Silent no-op: every caller treats null as "print nothing", which is
        // what we want rather than surfacing HTML in the chat log.
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

    /// <summary>
    /// One pass over the array-API endpoints, then the <c>/m</c> scrape as a
    /// last resort. <c>SawMarkup</c> distinguishes "Google gave us junk" (worth
    /// retrying) from "the requests failed" (not worth it).
    /// </summary>
    private async Task<(TranslationResult? Result, bool SawMarkup)> TranslateOnceAsync(
        string query,
        string text,
        string sl,
        string tl,
        bool wantTransliteration,
        int offset,
        CancellationToken ct)
    {
        var (result, sawMarkup) = await TryGtxEndpointsAsync(
            query, wantTransliteration, offset, "Translate", ct).ConfigureAwait(false);
        if (result != null)
            return (result, sawMarkup);

        // The mobile page carries no romanization, so it can only ever answer
        // the translation half. Reaching it means this message prints without
        // its romaji/pinyin.
        var mobile = await TranslateWithMobileAsync(text, sl, tl, ct).ConfigureAwait(false);
        if (mobile == null)
            return (null, sawMarkup);

        if (TranslationSanity.IsSuspect(mobile.Translated))
        {
            DebugLog($"Translate mobile discarded: markup in response '{TranslationSanity.Preview(mobile.Translated)}'");
            return (null, true);
        }

        if (wantTransliteration)
            DebugLog("Translate fell through to the mobile page; no romaji/pinyin available for this message");

        return (DropSuspectTransliteration(mobile), sawMarkup);
    }

    /// <summary>
    /// Walks the array-API endpoints in rotated order, skipping any that are
    /// cooling down after a throttle. When <paramref name="requireTransliteration"/>
    /// is set, a result that carries only a translation is held back as a
    /// fallback so a later endpoint still gets the chance to supply the
    /// romanization — we return the partial answer only if none of them do.
    /// </summary>
    private async Task<(TranslationResult? Result, bool SawMarkup)> TryGtxEndpointsAsync(
        string query,
        bool requireTransliteration,
        int offset,
        string label,
        CancellationToken ct)
    {
        var sawMarkup = false;
        TranslationResult? partial = null;

        for (var i = 0; i < GtxEndpoints.Length; i++)
        {
            var endpoint = GtxEndpoints[(i + offset) % GtxEndpoints.Length];
            if (IsThrottled(endpoint.Name))
                continue;

            var url = $"{endpoint.Url}?client={endpoint.Client}{query}";
            var result = await FetchGtxAsync(url, endpoint, label, ct).ConfigureAwait(false);
            if (result == null)
                continue;

            if (TranslationSanity.IsSuspect(result.Translated))
            {
                sawMarkup = true;
                DebugLog($"{label} {endpoint.Name} discarded: markup in response " +
                         $"'{TranslationSanity.Preview(result.Translated)}'");
                continue;
            }

            result = DropSuspectTransliteration(result);

            // Held back rather than returned: another endpoint may still have
            // the romanization this one omitted.
            if (requireTransliteration && string.IsNullOrWhiteSpace(result.Transliteration))
            {
                partial ??= result;
                DebugLog($"{label} {endpoint.Name} returned no transliteration; trying the next endpoint");
                continue;
            }

            return (result, sawMarkup);
        }

        return (partial, sawMarkup);
    }

    private async Task<TranslationResult?> FetchGtxAsync(
        string url, GtxEndpoint endpoint, string label, CancellationToken ct)
    {
        try
        {
            using var res = await http.GetAsync(url, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                // 429/403 is this client id's quota, not ours as a whole. Park
                // it so the remaining messages skip straight to a live one.
                if (res.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden)
                {
                    MarkThrottled(endpoint.Name);
                    DebugLog($"{label} {endpoint.Name} throttled: HTTP {(int)res.StatusCode}; " +
                             $"skipping it for {ThrottleCooldown.TotalMinutes:0} min");
                }
                else
                {
                    DebugLog($"{label} {endpoint.Name} failed: HTTP {(int)res.StatusCode} {res.ReasonPhrase}");
                }
                return null;
            }

            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var parsed = ParseGtxResponse(body);
            if (parsed == null && LooksLikeGoogleBlock(body))
            {
                MarkThrottled(endpoint.Name);
                DebugLog($"{label} {endpoint.Name} failed: Google returned a traffic/CAPTCHA block page");
            }

            if (parsed != null)
                ClearThrottle(endpoint.Name);

            return parsed;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            DebugLog($"{label} {endpoint.Name} failed: {ex.Message}");
            return null;
        }
    }

    private bool IsThrottled(string name)
    {
        lock (throttleLock)
        {
            if (!throttledUntil.TryGetValue(name, out var until))
                return false;

            if (DateTime.UtcNow < until)
                return true;

            throttledUntil.Remove(name);
            return false;
        }
    }

    private void MarkThrottled(string name)
    {
        lock (throttleLock)
            throttledUntil[name] = DateTime.UtcNow + ThrottleCooldown;
    }

    private void ClearThrottle(string name)
    {
        lock (throttleLock)
            throttledUntil.Remove(name);
    }

    // Romanization is decorative, so bad transliteration costs us the pinyin /
    // romaji hint but never the translation itself.
    private static TranslationResult DropSuspectTransliteration(TranslationResult result) =>
        TranslationSanity.IsSuspect(result.Transliteration)
            ? result with { Transliteration = string.Empty }
            : result;

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

            // gtx routinely returns HTML entities inside otherwise valid output
            // ("don&#39;t"), so decode before anyone inspects or displays it.
            var translatedStr = TranslationSanity.Normalize(translated.ToString());
            var translitStr = TranslationSanity.Normalize(translit.ToString());
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

        // Drop script/style bodies outright — stripping only their tags would
        // leave the raw JS/CSS behind as if it were translated text. Singleline
        // so tags and blocks broken across newlines still match.
        html = Regex.Replace(html, "<(script|style)\\b.*?</\\1>", string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var withoutTags = Regex.Replace(html, "<.*?>", string.Empty, RegexOptions.Singleline);
        return TranslationSanity.Normalize(withoutTags);
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

    private record GtxEndpoint(string Name, string Url, string Client);
}

public record TranslationResult(string Translated, string Transliteration, string DetectedSource);
