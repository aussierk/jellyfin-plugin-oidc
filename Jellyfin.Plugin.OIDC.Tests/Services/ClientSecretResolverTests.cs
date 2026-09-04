using System;
using System.IO;
using Jellyfin.Plugin.OIDC.Configuration;
using Jellyfin.Plugin.OIDC.Services;
using Xunit;

namespace Jellyfin.Plugin.OIDC.Tests.Services;

public class ClientSecretResolverTests
{
    private static OidcProviderConfig Provider(string secret = "", string secretFile = "")
        => new() { ProviderId = "p1", ClientSecret = secret, ClientSecretFile = secretFile };

    [Fact]
    public void Resolve_NoFileNoEnvRef_ReturnsClientSecretVerbatim()
        => Assert.Equal("plain-secret", ClientSecretResolver.Resolve(Provider(secret: "plain-secret")));

    [Fact]
    public void Resolve_ClientSecretFile_ReturnsTrimmedFileContent()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "  secret-from-file  \n");
            var result = ClientSecretResolver.Resolve(Provider(secret: "ignored", secretFile: path));
            Assert.Equal("secret-from-file", result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Resolve_ClientSecretFileMissing_FallsBackToClientSecret()
    {
        var result = ClientSecretResolver.Resolve(
            Provider(secret: "fallback-secret", secretFile: Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));

        Assert.Equal("fallback-secret", result);
    }

    [Fact]
    public void Resolve_ClientSecretIsEnvVarReference_ResolvesFromEnvironment()
    {
        var varName = "OIDC_TEST_SECRET_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(varName, "secret-from-env");
        try
        {
            var result = ClientSecretResolver.Resolve(Provider(secret: "${" + varName + "}"));
            Assert.Equal("secret-from-env", result);
        }
        finally
        {
            Environment.SetEnvironmentVariable(varName, null);
        }
    }

    [Fact]
    public void Resolve_EnvVarReferenceNotSet_ReturnsLiteralReference()
    {
        var varName = "OIDC_TEST_SECRET_UNSET_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(varName, null);

        var result = ClientSecretResolver.Resolve(Provider(secret: "${" + varName + "}"));

        Assert.Equal("${" + varName + "}", result);
    }

    [Fact]
    public void Resolve_NotAnExactEnvVarPattern_ReturnsLiteral()
        => Assert.Equal("prefix-${NOT_A_REF}-suffix",
            ClientSecretResolver.Resolve(Provider(secret: "prefix-${NOT_A_REF}-suffix")));

    [Fact]
    public void Resolve_MultipleProviders_ResolveIndependently()
    {
        // Two providers, each with its own file and its own env var — resolving one must never
        // leak into or be affected by the other.
        var pathA = Path.GetTempFileName();
        var varB = "OIDC_TEST_SECRET_B_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(varB, "secret-b");
        try
        {
            File.WriteAllText(pathA, "secret-a");
            var providerA = new OidcProviderConfig { ProviderId = "keycloak", ClientSecretFile = pathA };
            var providerB = new OidcProviderConfig { ProviderId = "authentik", ClientSecret = "${" + varB + "}" };

            Assert.Equal("secret-a", ClientSecretResolver.Resolve(providerA));
            Assert.Equal("secret-b", ClientSecretResolver.Resolve(providerB));
            // Re-resolving A after B must still return A's own secret.
            Assert.Equal("secret-a", ClientSecretResolver.Resolve(providerA));
        }
        finally
        {
            File.Delete(pathA);
            Environment.SetEnvironmentVariable(varB, null);
        }
    }

    [Fact]
    public void Resolve_ClientSecretFileTakesPriorityOverEnvRef()
    {
        var path = Path.GetTempFileName();
        var varName = "OIDC_TEST_SECRET_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(varName, "should-not-be-used");
        try
        {
            File.WriteAllText(path, "from-file-wins");
            var result = ClientSecretResolver.Resolve(Provider(secret: "${" + varName + "}", secretFile: path));
            Assert.Equal("from-file-wins", result);
        }
        finally
        {
            File.Delete(path);
            Environment.SetEnvironmentVariable(varName, null);
        }
    }
}
