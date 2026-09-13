using System;
using System.Text;
using Jellyfin.Plugin.OIDC.Services;
using Xunit;

namespace Jellyfin.Plugin.OIDC.Tests.Services;

public class ProviderButtonAssetsTests
{
    // ── CustomBrandColor ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("#1A2B3C")]
    [InlineData("#abc")]
    [InlineData("#11223344")]
    [InlineData("rebeccapurple")]
    public void CustomBrandColor_ValidNonDefault_ReturnsTrimmedValue(string color)
        => Assert.Equal(color, ProviderButtonAssets.CustomBrandColor("  " + color + "  "));

    [Theory]
    [InlineData("#4285F4")]   // config default - let the theme colour it
    [InlineData("#4285f4")]   // default, case-insensitive
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("javascript:alert(1)")]
    [InlineData("red; background:url(x)")]
    [InlineData("#12")]       // too short
    [InlineData("#1234567890")] // too long
    public void CustomBrandColor_DefaultOrUnsafeOrBlank_ReturnsNull(string? color)
        => Assert.Null(ProviderButtonAssets.CustomBrandColor(color));

    // ── IconDataUri ──────────────────────────────────────────────────────────

    [Fact]
    public void IconDataUri_KnownBundledKey_ReturnsDataUri()
    {
        var result = ProviderButtonAssets.IconDataUri("authentik");

        Assert.NotNull(result);
        Assert.StartsWith("data:image/svg+xml;base64,", result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("not-a-real-icon")]
    public void IconDataUri_BlankOrUnknownKey_ReturnsNull(string? icon)
        => Assert.Null(ProviderButtonAssets.IconDataUri(icon));

    [Theory]
    [InlineData("data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8Xw8AAoMBgDTD2qgAAAAASUVORK5CYII=")]
    [InlineData("data:image/gif;base64,R0lGODlhAQABAIAAAP///wAAACwAAAAAAQABAAACAkQBADs=")]
    public void IconDataUri_ValidImageDataUri_PassesThrough(string dataUri)
        => Assert.Equal(dataUri, ProviderButtonAssets.IconDataUri(dataUri));

    [Theory]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("data:application/octet-stream;base64,AAAA")]
    public void IconDataUri_NonImageDataUri_ReturnsNull(string dataUri)
        => Assert.Null(ProviderButtonAssets.IconDataUri(dataUri));

    [Fact]
    public void IconDataUri_OversizeDataUri_ReturnsNull()
        => Assert.Null(ProviderButtonAssets.IconDataUri("data:image/png;base64," + new string('A', 300_000)));

    [Fact]
    public void IconDataUri_OversizeRawSvg_ReturnsNull()
    {
        // Regression: the raw-<svg>-then-base64-encode path enforced no size cap at all,
        // unlike the data: URI passthrough path just above.
        var oversizeSvg = "<svg><path d=\"" + new string('M', 300_000) + "\"/></svg>";

        Assert.Null(ProviderButtonAssets.IconDataUri(oversizeSvg));
    }

    [Fact]
    public void IconDataUri_RawSvg_StripsScriptsAndHandlers()
    {
        var result = ProviderButtonAssets.IconDataUri(
            "<svg onload=\"alert(1)\"><script>alert(2)</script><path d=\"M0 0h1v1z\"/></svg>");

        var decoded = Decode(result);
        Assert.DoesNotContain("onload", decoded, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script", decoded, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", decoded);
    }

    [Fact]
    public void IconDataUri_UnclosedScriptTag_IsStrippedToEndOfString()
    {
        // Regression: <script...> with no closing </script> anywhere used to survive stripping.
        var decoded = Decode(ProviderButtonAssets.IconDataUri("<svg><script>alert(document.cookie)"));

        Assert.DoesNotContain("<script", decoded, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", decoded);
    }

    [Fact]
    public void IconDataUri_SvgWithXmlProlog_DropsPrologBeforeEncoding()
    {
        var file = "<?xml version=\"1.0\"?>\n<!-- gen -->\n<svg xmlns=\"http://www.w3.org/2000/svg\"><path d=\"M0 0h1v1z\"/></svg>";

        var decoded = Decode(ProviderButtonAssets.IconDataUri(file));

        Assert.StartsWith("<svg", decoded);
    }

    // ── IconDataUri base64-SVG ────────────────────────────────────────────────

    [Fact]
    public void IconDataUri_Base64EncodedSvgWithScript_StripsScriptBeforeAccepting()
    {
        var svg = "<svg onload=\"alert(1)\"><script>alert(2)</script><path d=\"M0 0h1v1z\"/></svg>";
        var base64 = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));

        var decoded = Decode(ProviderButtonAssets.IconDataUri(base64));

        Assert.DoesNotContain("onload", decoded, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script", decoded, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", decoded);
    }

    [Fact]
    public void IconDataUri_MalformedBase64SvgDataUri_ReturnsNullNotThrows()
        => Assert.Null(ProviderButtonAssets.IconDataUri("data:image/svg+xml;base64,not-valid-base64!!!"));

    [Fact]
    public void IconDataUri_Base64SvgWithNoSvgTagInDecodedPayload_ReturnsNull()
    {
        var base64 = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes("not an svg at all"));

        Assert.Null(ProviderButtonAssets.IconDataUri(base64));
    }

    [Fact]
    public void IconDataUri_Base64SvgOversizeAfterDecode_ReturnsNull()
    {
        var oversizeSvg = "<svg><path d=\"" + new string('M', 300_000) + "\"/></svg>";
        var base64 = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(oversizeSvg));

        Assert.Null(ProviderButtonAssets.IconDataUri(base64));
    }

    private static string Decode(string? dataUri)
    {
        Assert.NotNull(dataUri);
        var b64 = System.Text.RegularExpressions.Regex.Match(dataUri, @"base64,([A-Za-z0-9+/=]+)").Groups[1].Value;
        return Encoding.UTF8.GetString(Convert.FromBase64String(b64));
    }
}
