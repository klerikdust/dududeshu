using System;
using System.Net;
using System.Text.RegularExpressions;

namespace SamplePlugin.Services;

/// <summary>
/// Guards against Google handing us page markup or API scaffolding in place of
/// a translation. Both endpoints we use are unofficial and can fail this way:
/// gtx sometimes answers with a parseable array whose payload is really an
/// interstitial or HTML-escaped fragment, and the <c>/m</c> scrape can pick up
/// leftover markup whenever that page's layout shifts. Anything that trips
/// <see cref="IsSuspect"/> is discarded rather than shown to the user.
/// </summary>
internal static class TranslationSanity
{
    // Named elements only. A bare "<" is common in chat ("<3", "<Tank>", auto
    // translate brackets), so requiring a known tag name keeps those safe.
    private static readonly Regex HtmlTag = new(
        @"<\s*/?\s*(?:a|b|i|u|p|br|hr|em|strong|font|small|big|span|div|section|article|header|footer|" +
        @"nav|main|table|tbody|thead|tfoot|tr|td|th|ul|ol|li|dl|dt|dd|img|form|input|button|textarea|" +
        @"select|option|label|script|style|link|meta|base|title|head|body|html|iframe|noscript|center|" +
        @"pre|code|blockquote|h[1-6])\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // Any well-formed closing tag, which no natural-language output produces.
    private static readonly Regex ClosingTag = new(
        @"</\s*[A-Za-z][\w:.-]*\s*>",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex HtmlEntity = new(
        @"&(?:#\d{2,7}|#[xX][0-9A-Fa-f]{2,6}|nbsp|amp|quot|apos|lt|gt|copy|reg|trade|hellip|" +
        @"mdash|ndash|rsquo|lsquo|ldquo|rdquo|middot|bull|times);",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // Substrings that only ever appear in a Google page or API envelope. Kept
    // deliberately specific: a false positive silently drops a real message.
    private static readonly string[] ScaffoldingMarkers =
    {
        "<!doctype",
        "<!--",
        "<![cdata[",
        "translate.googleapis.com",
        "translate.google.com",
        "googleusercontent.com",
        "gstatic.com",
        "www.w3.org",
        "af_initdatacallback",
        "client=gtx",
        "result-container",
        "unusual traffic",
        "captcha",
        "our systems have detected",
        "text/javascript",
        "text/css",
        "charset=utf-8",
        "window.location",
        "document.getelement",
        "xmlhttprequest",
        "please enable javascript",
        "enablejsdialog",
    };

    /// <summary>
    /// Decodes HTML entities Google leaves in otherwise valid output (gtx
    /// regularly returns <c>&amp;#39;</c> for an apostrophe) and trims. Runs a
    /// second pass only when the first leaves entities behind, which is how
    /// double-encoded scaffolding shows up.
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var decoded = WebUtility.HtmlDecode(text);
        if (HtmlEntity.IsMatch(decoded))
            decoded = WebUtility.HtmlDecode(decoded);

        return decoded.Trim();
    }

    /// <summary>
    /// True when the text looks like markup or API plumbing rather than a
    /// translation. Blank input is not suspect — callers treat empty as its own
    /// "no result" case.
    /// </summary>
    public static bool IsSuspect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (ClosingTag.IsMatch(text) || HtmlTag.IsMatch(text))
            return true;

        // Entities that survived two decode passes were never real text.
        if (HtmlEntity.IsMatch(text))
            return true;

        foreach (var marker in ScaffoldingMarkers)
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Truncated, single-line form for debug logs.</summary>
    public static string Preview(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var flat = text.Replace('\r', ' ').Replace('\n', ' ');
        return flat.Length > 120 ? flat[..120] + "…" : flat;
    }
}
