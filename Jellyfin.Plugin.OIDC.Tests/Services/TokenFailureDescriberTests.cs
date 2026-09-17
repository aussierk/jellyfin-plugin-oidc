using Jellyfin.Plugin.OIDC.Services;
using Xunit;

namespace Jellyfin.Plugin.OIDC.Tests.Services;

public class TokenFailureDescriberTests
{
    [Theory]
    [InlineData("IDX10249: The certificate has expired.", "signing certificate has expired")]
    [InlineData("IDX10214: Audience validation failed.", "audience doesn't match")]
    [InlineData("IDX10205: Issuer validation failed.", "issuer doesn't match")]
    [InlineData("IDX10223: Lifetime validation failed. The token is expired.", "clock drift")]
    [InlineData("IDX10222: Lifetime validation failed.", "clock drift")]
    [InlineData("IDX10225: Lifetime validation failed.", "clock drift")]
    [InlineData("IDX10500: Signature validation failed. Unable to resolve SecurityKeyIdentifier.", "no matching signing key")]
    [InlineData("IDX10511: Signature validation failed.", "no matching signing key")]
    [InlineData("IDX10517: Signature validation failed. The algorithm is not supported.", "supported algorithm")]
    public void Describe_KnownCode_ReturnsMatchingHint(string error, string expectedSubstring)
        => Assert.Contains(expectedSubstring, TokenFailureDescriber.Describe(error));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("some unrelated failure with no IDX code")]
    public void Describe_UnknownOrMissing_ReturnsNull(string? error)
        => Assert.Null(TokenFailureDescriber.Describe(error));
}
