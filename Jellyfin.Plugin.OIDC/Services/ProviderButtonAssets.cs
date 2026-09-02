using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.OIDC.Services;

/// <summary>
/// Sanitizes a provider's admin-set button styling - brand colour and icon - into values
/// safe to drop into CSS. Shared by <see cref="LoginButtonSnippetBuilder"/> (the Branding
/// snippet) and the anonymous <c>GetProviders</c> API, so both apply the same rules.
/// This is NOT a general-purpose SVG sanitizer - it only strips known script-execution
/// vectors (see <c>_scriptOrHandler</c> / <c>_dangerousSvgConstructs</c>). Safety depends
/// entirely on the sanitized icon only ever being consumed as a CSS
/// <c>background-image: url(...)</c> value; re-audit this file before inserting the result
/// into any DOM/script context.
/// </summary>
public static class ProviderButtonAssets
{
    private const string DefaultButtonColor = "#4285F4";

    // #RGB / #RGBA / #RRGGBB / #RRGGBBAA, or a CSS named colour (letters/hyphens only).
    private static readonly Regex _safeCssColor = new(
        @"^(#[0-9a-fA-F]{3,8}|[a-zA-Z\-]+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // The (</script\s*>|$) alternation strips to end-of-string even if a truncated tag has no closing </script>.
    private static readonly Regex _scriptOrHandler = new(
        @"<script[\s\S]*?(</script\s*>|$)|\son[a-zA-Z]+\s*=\s*(""[^""]*""|'[^']*'|[^\s>]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Script/navigation vectors _scriptOrHandler misses: javascript: URIs in any attribute,
    // <foreignObject>, <set> (SMIL that sets a handler attribute), CSS @import. Not a general SVG
    // sanitizer (see the class summary); safe only because the result is consumed as a CSS url().
    private static readonly Regex _dangerousSvgConstructs = new(
        @"javascript\s*:|<foreignObject[\s\S]*?(</foreignObject\s*>|$)|<set\b[^>]*>|@import",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Accepted image data-URI types: SVG plus the common raster formats.
    private static readonly Regex _imageDataUri = new(
        @"^data:image/(svg\+xml|png|jpe?g|gif|webp)[;,]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Cap the encoded icon so it can't bloat the Branding Custom CSS (~192 KB of image).
    private const int MaxIconDataUriLength = 262_144;

    /// The provider's brand colour when valid and non-default; null lets the theme colour the button.
    public static string? CustomBrandColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return null;
        }

        var trimmed = color.Trim();
        return _safeCssColor.IsMatch(trimmed)
               && !string.Equals(trimmed, DefaultButtonColor, StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : null;
    }

    /// 
    /// Resolves <c>ButtonIcon</c> (bundled key, raw <c>&lt;svg&gt;</c>, or a <c>data:image/...</c>
    /// URI) to a sanitized <c>data:</c> URI, or null. Scripts/event handlers are stripped and the
    /// encoded result is size-bounded.
    /// 
    public static string? IconDataUri(string? buttonIcon)
    {
        if (string.IsNullOrWhiteSpace(buttonIcon))
        {
            return null;
        }

        var v = buttonIcon.Trim();

        if (v.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return _imageDataUri.IsMatch(v)
                   && v.Length <= MaxIconDataUriLength
                   && !_scriptOrHandler.IsMatch(v)
                   && !_dangerousSvgConstructs.IsMatch(v)
                   && v.IndexOf('"') < 0
                   && v.IndexOf(')') < 0
                ? v
                : null;
        }

        // Skip any <?xml?>/<!DOCTYPE> prolog a pasted .svg file may carry.
        var svgStart = v.IndexOf("<svg", StringComparison.OrdinalIgnoreCase);
        if (svgStart >= 0)
        {
            var cleaned = _scriptOrHandler.Replace(v.Substring(svgStart), string.Empty);
            cleaned = _dangerousSvgConstructs.Replace(cleaned, string.Empty);
            var bytes = Encoding.UTF8.GetBytes(cleaned);
            var encoded = "data:image/svg+xml;base64," + Convert.ToBase64String(bytes);
            return encoded.Length <= MaxIconDataUriLength ? encoded : null;
        }

        return KnownProviderIcons.TryGet(v);
    }
}
