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
        ["__TOKEN_JSON__"] = "\"tok\"",
        ["__PROVIDER_ID_JSON__"] = "\"kc\"",
        ["__DEVICE_ID_KEY_JSON__"] = "\"_deviceId2\"",
        ["__CREDENTIALS_KEY_JSON__"] = "\"jellyfin_credentials\"",
        ["__APP_NAME_JSON__"] = "\"Jellyfin Web\"",
        ["__APP_VERSION_JSON__"] = "\"10.11.0\""
    };

    [Fact]
    public void Render_FillsEveryPlaceholder_NoneLeftBehind()
    {
        var html = EmbeddedPage.Render("CallbackPage.html", CallbackValues);

        Assert.DoesNotContain("__", html);
        Assert.Contains("<style nonce=\"nonce-xyz\">", html);
        Assert.Contains("const token = \"tok\";", html);
    }

    [Fact]
    public void Render_QuickConnectPage_ResourceResolves()
    {
        var html = EmbeddedPage.Render("QuickConnectPage.html", new Dictionary<string, string>
        {
            ["__CSP_NONCE__"] = "n",
            ["__TOKEN_JSON__"] = "\"t\"",
            ["__PROVIDER_ID_JSON__"] = "\"p\""
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
        incomplete.Remove("__APP_VERSION_JSON__");

        var ex = Assert.Throws<InvalidOperationException>(
            () => EmbeddedPage.Render("CallbackPage.html", incomplete));
        Assert.Contains("__APP_VERSION_JSON__", ex.Message);
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
        // The token value literally spells another slot's placeholder. A multi-pass replace
        // would then overwrite it with the app name; a single pass leaves it intact.
        var values = new Dictionary<string, string>(CallbackValues)
        {
            ["__TOKEN_JSON__"] = "\"__APP_NAME_JSON__\"",
            ["__APP_NAME_JSON__"] = "\"RealApp\""
        };

        var html = EmbeddedPage.Render("CallbackPage.html", values);

        Assert.Contains("const token = \"__APP_NAME_JSON__\";", html);
        Assert.Contains("App: \"RealApp\"", html);
    }
}
