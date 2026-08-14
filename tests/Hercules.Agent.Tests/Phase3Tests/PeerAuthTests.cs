using System.Security.Cryptography;
using System.Text;
using Hercules.Mesh.Auth;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.Phase3Tests;

public class PeerAuthTests
{
    private readonly PeerAuthConfig _config;
    private readonly Mock<ILogger<TokenIssuer>> _issuerLogger = new();
    private readonly Mock<ILogger<BearerIdentityProvider>> _bearerLogger = new();
    private readonly Mock<ILogger<ApiKeyIdentityProvider>> _apikeyLogger = new();
    private readonly Mock<ILogger<MTlsIdentityProvider>> _mtlsLogger = new();
    private readonly Mock<ILogger<DefaultPeerCredentialProvider>> _providerLogger = new();

    public PeerAuthTests()
    {
        _config = new PeerAuthConfig
        {
            TokenSecret = "test-secret-32-bytes-long-1234567890",
            TokenTtlSeconds = 60,
            MaxDelegationDepth = 3,
            OutboundMode = "bearer",
            DefaultScopes = new List<string> { "delegate:read", "delegate:write" }
        };
    }

    // === TokenIssuer tests ===

    [Fact]
    public void TokenIssuer_Issue_AndVerify_RoundTrip()
    {
        var issuer = new TokenIssuer(_config, _issuerLogger.Object);

        var token = issuer.Issue(
            subject: "agent/hercules",
            audience: "agent/peer1",
            scope: "delegate:read");

        Assert.NotNull(token);
        Assert.Contains(".", token);

        var verified = issuer.Verify(token);
        Assert.NotNull(verified);
        Assert.Equal("agent/hercules", verified!.Subject);
        Assert.Equal("agent/peer1", verified.Audience);
        Assert.Equal("delegate:read", verified.Scope);
        Assert.Equal(0, verified.DelegationDepth);
        Assert.False(verified.IsExpired);
    }

    [Fact]
    public void TokenIssuer_RejectsTamperedToken()
    {
        var issuer = new TokenIssuer(_config, _issuerLogger.Object);
        var token = issuer.Issue("agent/hercules", "agent/peer1", "delegate:read");

        // Tamper with the payload: flip a character in the base64 payload
        var parts = token.Split('.');
        var payloadChars = parts[0].ToCharArray();
        payloadChars[0] = payloadChars[0] == 'A' ? 'B' : 'A';
        var tampered = new string(payloadChars) + "." + parts[1];

        Assert.Throws<AuthenticationException>(() => issuer.Verify(tampered));
    }

    [Fact]
    public void TokenIssuer_RejectsExpiredToken()
    {
        var shortConfig = new PeerAuthConfig
        {
            TokenSecret = "test-secret-32-bytes-long-1234567890",
            TokenTtlSeconds = -1, // already expired
            MaxDelegationDepth = 3
        };
        var issuer = new TokenIssuer(shortConfig, _issuerLogger.Object);
        var token = issuer.Issue("agent/hercules", "agent/peer1", "delegate:read");

        Assert.Throws<AuthenticationException>(() => issuer.Verify(token));
    }

    [Fact]
    public void TokenIssuer_RejectsExcessiveDepth()
    {
        var issuer = new TokenIssuer(_config, _issuerLogger.Object);

        // Issue with depth > max
        Assert.Throws<InvalidOperationException>(() =>
            issuer.Issue("agent/hercules", "agent/peer1", "delegate:read", delegationDepth: 5));
    }

    [Fact]
    public void TokenIssuer_Delegate_IncrementsDepthAndReducesScope()
    {
        var issuer = new TokenIssuer(_config, _issuerLogger.Object);
        var parent = issuer.Issue("agent/root", "agent/hercules",
            "delegate:read,delegate:write,delegate:admin",
            delegationDepth: 0, rootRequestId: "root-req-1");

        var child = issuer.Delegate(parent, audience: "agent/peer1",
            reducedScope: "delegate:read", rootRequestId: "root-req-1");

        var verified = issuer.Verify(child);
        Assert.NotNull(verified);
        Assert.Equal(1, verified!.DelegationDepth);
        Assert.Equal("delegate:read", verified.Scope);
        Assert.Equal("root-req-1", verified.RootRequestId);
    }

    [Fact]
    public void TokenIssuer_Delegate_RejectsScopeNotSubset()
    {
        var issuer = new TokenIssuer(_config, _issuerLogger.Object);
        var parent = issuer.Issue("agent/root", "agent/hercules",
            scope: "delegate:read");

        Assert.Throws<AuthenticationException>(() =>
            issuer.Delegate(parent, "agent/peer1", "delegate:admin", "root-1"));
    }

    [Fact]
    public void IsScopeSubset_HandlesEdgeCases()
    {
        Assert.True(TokenIssuer.IsScopeSubset("", "delegate:read"));
        Assert.True(TokenIssuer.IsScopeSubset("read", "read,write"));
        Assert.True(TokenIssuer.IsScopeSubset("READ", "read"));
        Assert.False(TokenIssuer.IsScopeSubset("read", ""));
        Assert.False(TokenIssuer.IsScopeSubset("read,admin", "read"));
    }

    [Fact]
    public void TokenIssuer_GeneratesRandomSecret_WhenEmpty()
    {
        var emptyConfig = new PeerAuthConfig { TokenSecret = "", TokenTtlSeconds = 60 };
        var issuer = new TokenIssuer(emptyConfig, _issuerLogger.Object);
        var token = issuer.Issue("a", "b", "read");
        var verified = issuer.Verify(token);
        Assert.NotNull(verified);
    }

    // === BearerIdentityProvider tests ===

    [Fact]
    public async Task BearerIdentityProvider_VerifiesValidToken()
    {
        var issuer = new TokenIssuer(_config, _issuerLogger.Object);
        var provider = new BearerIdentityProvider(issuer, _bearerLogger.Object);

        var token = issuer.Issue("agent/hercules", "agent/peer1", "delegate:read");
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {token}"
        };

        var result = await provider.AuthenticateAsync(headers);

        Assert.NotNull(result);
        Assert.Equal("agent/hercules", result!.Subject);
        Assert.Equal("bearer", result.AuthMethod);
        Assert.Contains("delegate:read", result.Scopes);
    }

    [Fact]
    public async Task BearerIdentityProvider_ReturnsNull_WhenNoAuthHeader()
    {
        var issuer = new TokenIssuer(_config, _issuerLogger.Object);
        var provider = new BearerIdentityProvider(issuer, _bearerLogger.Object);

        var result = await provider.AuthenticateAsync(new Dictionary<string, string>());

        Assert.Null(result);
    }

    [Fact]
    public async Task BearerIdentityProvider_ReturnsNull_WhenNonBearerScheme()
    {
        var issuer = new TokenIssuer(_config, _issuerLogger.Object);
        var provider = new BearerIdentityProvider(issuer, _bearerLogger.Object);

        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = "Basic dXNlcjpwYXNz"
        };

        var result = await provider.AuthenticateAsync(headers);
        Assert.Null(result);
    }

    [Fact]
    public async Task BearerIdentityProvider_Throws_WhenTokenInvalid()
    {
        var issuer = new TokenIssuer(_config, _issuerLogger.Object);
        var provider = new BearerIdentityProvider(issuer, _bearerLogger.Object);

        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer invalid.token.here"
        };

        await Assert.ThrowsAsync<AuthenticationException>(() =>
            provider.AuthenticateAsync(headers));
    }

    // === ApiKeyIdentityProvider tests ===

    [Fact]
    public async Task ApiKeyIdentityProvider_AcceptsValidKey()
    {
        var config = new PeerAuthConfig
        {
            PeerKeys = new Dictionary<string, string>
            {
                ["peer1"] = "secret-key-12345"
            }
        };
        var provider = new ApiKeyIdentityProvider(config, _apikeyLogger.Object);

        var headers = new Dictionary<string, string>
        {
            ["X-Api-Key"] = "secret-key-12345",
            ["X-Agent-Id"] = "peer1"
        };

        var result = await provider.AuthenticateAsync(headers);

        Assert.NotNull(result);
        Assert.Equal("agent/peer1", result!.Subject);
        Assert.Equal("apikey", result.AuthMethod);
    }

    [Fact]
    public async Task ApiKeyIdentityProvider_RejectsInvalidKey()
    {
        var config = new PeerAuthConfig
        {
            PeerKeys = new Dictionary<string, string> { ["peer1"] = "secret-key-12345" }
        };
        var provider = new ApiKeyIdentityProvider(config, _apikeyLogger.Object);

        var headers = new Dictionary<string, string>
        {
            ["X-Api-Key"] = "wrong-key",
            ["X-Agent-Id"] = "peer1"
        };

        await Assert.ThrowsAsync<AuthenticationException>(() =>
            provider.AuthenticateAsync(headers));
    }

    [Fact]
    public async Task ApiKeyIdentityProvider_RejectsUnknownAgent()
    {
        var config = new PeerAuthConfig
        {
            PeerKeys = new Dictionary<string, string> { ["peer1"] = "secret" }
        };
        var provider = new ApiKeyIdentityProvider(config, _apikeyLogger.Object);

        var headers = new Dictionary<string, string>
        {
            ["X-Api-Key"] = "secret",
            ["X-Agent-Id"] = "unknown-peer"
        };

        await Assert.ThrowsAsync<AuthenticationException>(() =>
            provider.AuthenticateAsync(headers));
    }

    [Fact]
    public async Task ApiKeyIdentityProvider_RequiresAgentIdHeader()
    {
        var provider = new ApiKeyIdentityProvider(_config, _apikeyLogger.Object);
        var headers = new Dictionary<string, string> { ["X-Api-Key"] = "any" };

        await Assert.ThrowsAsync<AuthenticationException>(() =>
            provider.AuthenticateAsync(headers));
    }

    [Fact]
    public async Task ApiKeyIdentityProvider_ReturnsNull_WhenNoApiKey()
    {
        var provider = new ApiKeyIdentityProvider(_config, _apikeyLogger.Object);
        var result = await provider.AuthenticateAsync(new Dictionary<string, string>());
        Assert.Null(result);
    }

    // === MTlsIdentityProvider tests ===

    [Fact]
    public async Task MTlsIdentityProvider_ExtractsCnFromSubject()
    {
        var provider = new MTlsIdentityProvider(_mtlsLogger.Object);
        var headers = new Dictionary<string, string>
        {
            ["X-Client-Cert-Subject"] = "CN=hercules-agent, O=Hercules, C=RU"
        };

        var result = await provider.AuthenticateAsync(headers);

        Assert.NotNull(result);
        Assert.Equal("agent/hercules-agent", result!.Subject);
        Assert.Equal("mtls", result.AuthMethod);
    }

    [Fact]
    public async Task MTlsIdentityProvider_ReturnsNull_WhenNoHeader()
    {
        var provider = new MTlsIdentityProvider(_mtlsLogger.Object);
        var result = await provider.AuthenticateAsync(new Dictionary<string, string>());
        Assert.Null(result);
    }

    // === DefaultPeerCredentialProvider tests ===

    [Fact]
    public async Task DefaultPeerCredentialProvider_IssueBearerToken()
    {
        var issuer = new TokenIssuer(_config, _issuerLogger.Object);
        var provider = new DefaultPeerCredentialProvider(
            _config, issuer, "hercules-main", _providerLogger.Object);

        var creds = await provider.ResolveAsync("peer1", auth: null);

        Assert.NotNull(creds);
        Assert.Equal("bearer", creds!.AuthMethod);
        Assert.NotNull(creds.BearerToken);
    }

    [Fact]
    public async Task DefaultPeerCredentialProvider_RespectsAuthContext()
    {
        var issuer = new TokenIssuer(_config, _issuerLogger.Object);
        var provider = new DefaultPeerCredentialProvider(
            _config, issuer, "hercules-main", _providerLogger.Object);

        var parentAuth = new Hercules.Mesh.Schema.AuthContext
        {
            DelegationDepth = 1,
            RootRequestId = "root-123"
        };

        var creds = await provider.ResolveAsync("peer1", parentAuth);

        Assert.NotNull(creds);
        Assert.NotNull(creds!.BearerToken);
        var verified = issuer.Verify(creds.BearerToken!);
        Assert.Equal(2, verified!.DelegationDepth);
        Assert.Equal("root-123", verified.RootRequestId);
    }

    [Fact]
    public async Task DefaultPeerCredentialProvider_RejectsExcessiveDepth()
    {
        var issuer = new TokenIssuer(_config, _issuerLogger.Object);
        var provider = new DefaultPeerCredentialProvider(
            _config, issuer, "hercules-main", _providerLogger.Object);

        var parentAuth = new Hercules.Mesh.Schema.AuthContext
        {
            DelegationDepth = 5 // already at max
        };

        var creds = await provider.ResolveAsync("peer1", parentAuth);
        Assert.Null(creds);
    }

    [Fact]
    public async Task DefaultPeerCredentialProvider_ResolvesApiKey()
    {
        var apiConfig = new PeerAuthConfig
        {
            OutboundMode = "apikey",
            PeerKeys = new Dictionary<string, string> { ["peer1"] = "test-key" }
        };
        var issuer = new TokenIssuer(apiConfig, _issuerLogger.Object);
        var provider = new DefaultPeerCredentialProvider(
            apiConfig, issuer, "hercules-main", _providerLogger.Object);

        var creds = await provider.ResolveAsync("peer1", null);

        Assert.NotNull(creds);
        Assert.Equal("apikey", creds!.AuthMethod);
        Assert.Equal("test-key", creds.CustomHeaders["X-Api-Key"]);
    }

    [Fact]
    public async Task DefaultPeerCredentialProvider_ReturnsNullForNoneMode()
    {
        var noneConfig = new PeerAuthConfig { OutboundMode = "none" };
        var issuer = new TokenIssuer(noneConfig, _issuerLogger.Object);
        var provider = new DefaultPeerCredentialProvider(
            noneConfig, issuer, "hercules-main", _providerLogger.Object);

        var creds = await provider.ResolveAsync("peer1", null);
        Assert.Null(creds);
    }

    // === PeerCredentials.Apply tests ===

    [Fact]
    public void PeerCredentials_AppliesBearerAndCustomHeaders()
    {
        var creds = new PeerCredentials
        {
            AuthMethod = "bearer",
            BearerToken = "test-token",
            CustomHeaders = new Dictionary<string, string>
            {
                ["X-Api-Key"] = "key",
                ["X-Delegation-Depth"] = "2"
            }
        };

        using var msg = new HttpRequestMessage();
        creds.Apply(msg.Headers);

        Assert.True(msg.Headers.Contains("Authorization"));
        Assert.True(msg.Headers.Contains("X-Api-Key"));
        Assert.True(msg.Headers.Contains("X-Delegation-Depth"));
    }

    [Fact]
    public void IdentityResult_HasScope_Works()
    {
        var identity = new IdentityResult
        {
            Subject = "x",
            AuthMethod = "bearer",
            Scopes = new[] { "delegate:read", "delegate:write" }
        };

        Assert.True(identity.HasScope("delegate:read"));
        Assert.True(identity.HasScope("DELEGATE:WRITE"));
        Assert.False(identity.HasScope("admin"));
    }

    [Fact]
    public void DelegationToken_LoadsScopesFromString()
    {
        var token = new DelegationToken { Scope = "a, b,c" };
        var scopes = token.GetScopes();

        Assert.Equal(3, scopes.Length);
        Assert.Contains("a", scopes);
        Assert.Contains("b", scopes);
        Assert.Contains("c", scopes);
    }

    [Fact]
    public void DelegationToken_HasScope_Works()
    {
        var token = new DelegationToken { Scope = "read,write" };
        Assert.True(token.HasScope("read"));
        Assert.True(token.HasScope("WRITE"));
        Assert.False(token.HasScope("admin"));
    }
}
