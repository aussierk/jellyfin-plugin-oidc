using System;
using System.Collections.Generic;
using Jellyfin.Plugin.OIDC.Services;
using Xunit;

namespace Jellyfin.Plugin.OIDC.Tests.Services;

public class EmbeddedPageTests
{
    private static readonly Dictionary<string, string> CallbackValues = new()
    {
        ["__CSP_NONCE__"] = "nonce-xyz",
        ["__DATA_JSON__"] = "{\"token\":\"tok\",\"providerId\":\"kc\"}"
    };

    [Fact]
    public void Render_FillsEveryPlaceholder_NoneLeftBehind()
    {
        var html = EmbeddedPage.Render("CallbackPage.html", CallbackValues);

        Assert.DoesNotContain("__", html);
        Assert.Contains("<style nonce=\"nonce-xyz\">", html);
        Assert.Contains(
            "<script type=\"application/json\" id=\"oidc-data\">{\"token\":\"tok\",\"providerId\":\"kc\"}</script>",
            html);
    }

    [Fact]
    public void Render_QuickConnectPage_ResourceResolves()
    {
        var html = EmbeddedPage.Render("QuickConnectPage.html", new Dictionary<string, string>
        {
            ["__CSP_NONCE__"] = "n",
            ["__DATA_JSON__"] = "{\"token\":\"t\",\"providerId\":\"p\"}"
        });

        Assert.Contains("id=\"code\"", html);
        Assert.DoesNotContain("__", html);
    }

    [Fact]
    public void Render_UnknownResource_Throws()
        => Assert.Throws<InvalidOperationException>(
            () => EmbeddedPage.Render("NoSuchPage.html", new Dictionary<string, string>()));

    [Fact]
    public void Render_MissingValueForDeclaredPlaceholder_Throws()
    {
        var incomplete = new Dictionary<string, string>(CallbackValues);
        incomplete.Remove("__DATA_JSON__");

        var ex = Assert.Throws<InvalidOperationException>(
            () => EmbeddedPage.Render("CallbackPage.html", incomplete));
        Assert.Contains("__DATA_JSON__", ex.Message);
    }

    [Fact]
    public void Render_ValueWithNoMatchingSlot_Throws()
    {
        var extra = new Dictionary<string, string>(CallbackValues) { ["__NOT_A_SLOT__"] = "x" };

        var ex = Assert.Throws<InvalidOperationException>(
            () => EmbeddedPage.Render("CallbackPage.html", extra));
        Assert.Contains("__NOT_A_SLOT__", ex.Message);
    }

    [Fact]
    public void Render_IsSinglePass_SubstitutedValueIsNotRescannedAsAnotherSlot()
    {
        // The data island literally spells the nonce slot's placeholder. A multi-pass replace
        // would then overwrite it with the nonce; a single pass leaves it intact.
        var values = new Dictionary<string, string>
        {
            ["__CSP_NONCE__"] = "realnonce",
            ["__DATA_JSON__"] = "{\"token\":\"__CSP_NONCE__\"}"
        };

        var html = EmbeddedPage.Render("CallbackPage.html", values);

        Assert.Contains("{\"token\":\"__CSP_NONCE__\"}", html);
        Assert.Contains("<style nonce=\"realnonce\">", html);
    }
}
