using Jellyfin.Plugin.OIDC.Configuration;
using Jellyfin.Plugin.OIDC.Services;
using Xunit;

namespace Jellyfin.Plugin.OIDC.Tests.Services;

public class AdmissionServiceTests
{
    private static string? Evaluate(PluginConfiguration cfg, string[] roles, string email, bool verified)
        => AdmissionService.Evaluate(cfg, roles, email, verified);

    [Fact]
    public void NoAllowlist_Admits()
        => Assert.Null(Evaluate(new PluginConfiguration(), ["anything"], string.Empty, false));

    [Fact]
    public void RequireVerifiedEmail_DeniesUnverified()
        => Assert.Equal("email-not-verified",
            Evaluate(new PluginConfiguration { RequireVerifiedEmail = true }, [], string.Empty, false));

    [Fact]
    public void GroupMatch_Admits()
        => Assert.Null(Evaluate(
            new PluginConfiguration { AllowedGroups = ["staff"] }, ["Staff"], string.Empty, false));

    [Fact]
    public void NoGroupMatch_Denies()
        => Assert.Equal("not-on-allowlist", Evaluate(
            new PluginConfiguration { AllowedGroups = ["staff"] }, ["guests"], string.Empty, true));

    [Fact]
    public void DomainMatch_WithVerifiedEmailRequired_Admits()
        => Assert.Null(Evaluate(
            new PluginConfiguration { RequireVerifiedEmail = true, AllowedEmailDomains = ["example.com"] },
            [], "user@example.com", true));

    [Fact]
    public void ExactEmailMatch_WithVerifiedEmailRequired_Admits()
        => Assert.Null(Evaluate(
            new PluginConfiguration { RequireVerifiedEmail = true, AllowedEmails = ["vip@example.com"] },
            [], "vip@example.com", true));

    [Fact]
    public void AllowedEmailDomains_InertWithoutRequireVerifiedEmail()
        => Assert.Null(Evaluate(
            new PluginConfiguration { AllowedEmailDomains = ["example.com"] },
            [], "someone@other.org", false));

    [Fact]
    public void DomainAndEmailBothSet_NoMatch_Denies()
        => Assert.Equal("not-on-allowlist", Evaluate(
            new PluginConfiguration { RequireVerifiedEmail = true, AllowedEmailDomains = ["example.com"] },
            [], "user@other.org", true));

    // ── subdomain matching (F2) ───────────────────────────────────────────────

    [Fact]
    public void SubdomainOfAllowedDomain_Admits()
        => Assert.Null(Evaluate(
            new PluginConfiguration { RequireVerifiedEmail = true, AllowedEmailDomains = ["example.com"] },
            [], "user@mail.example.com", true));

    [Fact]
    public void DeepSubdomainOfAllowedDomain_Admits()
        => Assert.Null(Evaluate(
            new PluginConfiguration { RequireVerifiedEmail = true, AllowedEmailDomains = ["example.com"] },
            [], "user@a.b.example.com", true));

    [Fact]
    public void SuffixButNotSubdomain_Denies()
        // "notexample.com" ends with "example.com" textually but is not a subdomain.
        => Assert.Equal("not-on-allowlist", Evaluate(
            new PluginConfiguration { RequireVerifiedEmail = true, AllowedEmailDomains = ["example.com"] },
            [], "user@notexample.com", true));

    [Fact]
    public void SubdomainMatch_IsCaseInsensitive()
        => Assert.Null(Evaluate(
            new PluginConfiguration { RequireVerifiedEmail = true, AllowedEmailDomains = ["Example.COM"] },
            [], "user@MAIL.example.com", true));
}
