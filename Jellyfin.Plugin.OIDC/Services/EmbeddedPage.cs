using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.OIDC.Services;

///
/// Renders an embedded HTML page (<c>Configuration/*.html</c>) by filling its
/// <c>__PLACEHOLDER__</c> slots in a single pass, so a value substituted for one slot can
/// never be rescanned as another slot. The caller owns encoding - a slot that lands inside a
/// script literal must be handed a value that is already a valid JS/JSON literal. Every slot
/// the template declares must be supplied, and every value supplied must be consumed;
/// a mismatch (typo'd placeholder name, stale key) throws rather than shipping a broken page.
///
public static class EmbeddedPage
{
    private static readonly Regex PlaceholderPattern =
        new("__[A-Z0-9_]+__", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string ResourcePrefix =
        typeof(EmbeddedPage).Assembly.GetName().Name + ".Configuration.";

    private static readonly ConcurrentDictionary<string, string> TemplateCache = new(StringComparer.Ordinal);

    /// <param name="fileName">Bare file name of the embedded page, e.g. <c>CallbackPage.html</c>.</param>
    /// <param name="values">One entry per <c>__PLACEHOLDER__</c> in the template, key including the underscores.</param>
    public static string Render(string fileName, IReadOnlyDictionary<string, string> values)
    {
        var template = TemplateCache.GetOrAdd(fileName, Load);

        var used = new HashSet<string>(StringComparer.Ordinal);
        var rendered = PlaceholderPattern.Replace(template, match =>
        {
            if (!values.TryGetValue(match.Value, out var replacement))
            {
                throw new InvalidOperationException(
                    $"Embedded page '{fileName}' declares placeholder {match.Value} but no value was supplied.");
            }

            used.Add(match.Value);
            return replacement;
        });

        var unused = values.Keys.Where(k => !used.Contains(k)).ToList();
        if (unused.Count > 0)
        {
            throw new InvalidOperationException(
                $"Embedded page '{fileName}' has no slot for value(s): {string.Join(", ", unused)}.");
        }

        return rendered;
    }

    private static string Load(string fileName)
    {
        var resourceName = ResourcePrefix + fileName;
        using var stream = typeof(EmbeddedPage).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        // Normalise so output is identical regardless of the checkout's line-ending policy.
        return reader.ReadToEnd().Replace("\r\n", "\n").TrimEnd('\n');
    }
}
