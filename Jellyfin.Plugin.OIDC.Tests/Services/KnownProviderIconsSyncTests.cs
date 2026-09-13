using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.OIDC.Services;
using Xunit;

namespace Jellyfin.Plugin.OIDC.Tests.Services;

/// <summary>
/// <see cref="KnownProviderIcons.Keys"/> (C#) and <c>ICON_KEYS</c> in the admin config script
/// (JS, drives the "Button Icon" dropdown and its custom-icon warning) are hand-duplicated.
/// Nothing at runtime keeps them equal, so this guard fails the build if they drift.
/// </summary>
public class KnownProviderIconsSyncTests
{
    private static string ConfigScript()
    {
        var path = Path.Combine(RepoRoot(), "Jellyfin.Plugin.OIDC", "Configuration", "oidcrbac.js");
        return File.ReadAllText(path);
    }

    // This source file is <repo>/Jellyfin.Plugin.OIDC.Tests/Services/<this>.cs - up three.
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Directory.GetParent(thisFile)!.Parent!.Parent!.FullName;

    [Fact]
    public void ConfigScript_IconKeysArrayMatchesKnownProviderIconsExactly()
    {
        // Set equality (not substring containment) catches an extra JS-only key too, not just a
        // C# key missing from JS. esbuild normalizes source-file single quotes to double quotes
        // in the bundle, so match on those, not the src/*.js quoting style.
        var match = Regex.Match(ConfigScript(), @"ICON_KEYS\s*=\s*\[([^\]]*)\]");
        Assert.True(match.Success, "Could not find 'ICON_KEYS = [...]' in the built oidcrbac.js");

        var jsKeys = Regex.Matches(match.Groups[1].Value, "\"([^\"]*)\"").Select(m => m.Groups[1].Value);

        Assert.Equal(
            new HashSet<string>(KnownProviderIcons.Keys),
            new HashSet<string>(jsKeys));
    }
}
