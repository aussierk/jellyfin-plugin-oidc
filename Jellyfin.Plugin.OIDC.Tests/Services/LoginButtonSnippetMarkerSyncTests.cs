using System.IO;
using System.Runtime.CompilerServices;
using Jellyfin.Plugin.OIDC.Services;
using Xunit;

namespace Jellyfin.Plugin.OIDC.Tests.Services;

///
/// The splice markers that fence the SSO button snippet live twice: as consts on
/// <see cref="LoginButtonSnippetBuilder"/> (C#, writes the snippet) and as literals in the
/// admin config script <c>oidcrbac.js</c> (JS, splices it into Branding). Nothing at runtime
/// keeps them equal, so this guard fails the build if they drift.
///
public class LoginButtonSnippetMarkerSyncTests
{
    private static string ConfigScript()
    {
        var path = Path.Combine(RepoRoot(), "Jellyfin.Plugin.OIDC", "Configuration", "oidcrbac.js");
        return File.ReadAllText(path);
    }

    // This source file is <repo>/Jellyfin.Plugin.OIDC.Tests/Services/<this>.cs - up three.
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Directory.GetParent(thisFile)!.Parent!.Parent!.FullName;

    [Theory]
    [InlineData(LoginButtonSnippetBuilder.HtmlStart)]
    [InlineData(LoginButtonSnippetBuilder.HtmlEnd)]
    [InlineData(LoginButtonSnippetBuilder.CssStart)]
    [InlineData(LoginButtonSnippetBuilder.CssEnd)]
    public void ConfigScript_ContainsEachBuilderMarkerVerbatim(string marker)
        => Assert.Contains('"' + marker + '"', ConfigScript());
}
