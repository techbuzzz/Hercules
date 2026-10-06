using Hercules.WebApi.Auth;
using Hercules.WebApi.Config;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.Auth;

/// <summary>
///     ADR-0009: unit-тесты <see cref="StudioSessionStore"/>.
///     Покрывает инварианты, благодаря которым Hercules Studio может не хранить
///     API-ключ в браузере: уникальность токена, отказ после TTL, различие
///     capability-наборов по роли, отзыв всех сессий.
/// </summary>
public class StudioSessionStoreTests
{
    private static StudioSessionStore Create(TimeSpan? ttl = null, TimeProvider? clock = null)
    {
        var cfg = new WebApiConfig();
        if (ttl is not null) cfg.StudioSessionTtlMinutes = (int)ttl.Value.TotalMinutes;
        return new StudioSessionStore(cfg, NullLogger<StudioSessionStore>.Instance, clock);
    }

    [Fact]
    public void Create_IssuesADistinctTokenPerSession()
    {
        var store = Create();

        var a = store.Create(ApiKeyRole.Contribute, "agent-1", "Agent One");
        var b = store.Create(ApiKeyRole.Contribute, "agent-1", "Agent One");

        Assert.NotEqual(a.Token, b.Token);
        Assert.True(a.Token.Length >= 43, "token should carry >=256 bits of entropy");
    }

    [Fact]
    public void Validate_AcceptsAFreshlyIssuedToken()
    {
        var store = Create();
        var session = store.Create(ApiKeyRole.Contribute, "agent-1", "Agent One");

        Assert.True(store.TryValidate(session.Token, out var found));
        Assert.NotNull(found);
        Assert.Equal(ApiKeyRole.Contribute, found!.Role);
        Assert.Equal("agent-1", found.AgentId);
    }

    [Fact]
    public void Validate_RejectsUnknownBlankAndNullTokens()
    {
        var store = Create();

        Assert.False(store.TryValidate("not-a-real-token", out _));
        Assert.False(store.TryValidate("", out _));
        Assert.False(store.TryValidate(null, out _));
    }

    [Fact]
    public void Validate_RejectsAfterExpiry_AndForgetsTheToken()
    {
        var clock = new FakeTimeProvider();
        var store = Create(ttl: TimeSpan.FromMinutes(30), clock: clock);

        var session = store.Create(ApiKeyRole.Contribute, "agent-1", "Agent One");
        Assert.True(store.TryValidate(session.Token, out _));

        clock.Advance(TimeSpan.FromMinutes(31));

        Assert.False(store.TryValidate(session.Token, out _));
        // A second attempt must still fail — expiry is not a soft warning.
        Assert.False(store.TryValidate(session.Token, out _));
    }

    [Fact]
    public void SystemRoleGrantsAStrictlyLargerCapabilitySet()
    {
        var store = Create();

        var contribute = store.Create(ApiKeyRole.Contribute, "a", "A");
        var system = store.Create(ApiKeyRole.System, "a", "A");

        Assert.Contains("config:patch", contribute.Capabilities);
        Assert.DoesNotContain("config:write", contribute.Capabilities);
        Assert.DoesNotContain("system:restart", contribute.Capabilities);

        Assert.Contains("config:write", system.Capabilities);
        Assert.Contains("system:restart", system.Capabilities);
        Assert.True(system.Capabilities.Count > contribute.Capabilities.Count);
    }

    [Fact]
    public void RoleIsCarriedOnTheSessionSoTheAgentCanAuthoriseIt()
    {
        var store = Create();

        var system = store.Create(ApiKeyRole.System, "agent-9", "Nine");
        Assert.True(store.TryValidate(system.Token, out var found));
        Assert.Equal(ApiKeyRole.System, found!.Role);
        Assert.Equal("agent-9", found.AgentId);
        Assert.Equal("Nine", found.DisplayName);
    }

    [Fact]
    public void RevokeAll_InvalidatesEveryOutstandingToken()
    {
        var store = Create();
        var a = store.Create(ApiKeyRole.Contribute, "a", "A");
        var b = store.Create(ApiKeyRole.System, "a", "A");

        Assert.Equal(2, store.RevokeAll());
        Assert.False(store.TryValidate(a.Token, out _));
        Assert.False(store.TryValidate(b.Token, out _));
    }

    [Fact]
    public void TtlFallsBackToThirtyMinutesWhenUnconfigured()
    {
        var store = Create(ttl: TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromMinutes(30), store.Ttl);
    }

    [Fact]
    public void ExpiresAtIsDerivedFromTheConfiguredTtl()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-06T10:00:00Z"));
        var store = Create(ttl: TimeSpan.FromMinutes(15), clock: clock);

        var session = store.Create(ApiKeyRole.Contribute, "a", "A");

        Assert.Equal(clock.GetUtcNow().AddMinutes(15), session.ExpiresAt);
    }

    // -----------------------------------------------------------------------
    //  Stage 6.3: sessions are bound to the API key that minted them, so a
    //  deleted or demoted key cannot keep admin rights until its TTL runs out.
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_RecordsTheMintingKeyFingerprint()
    {
        var store = Create();
        var session = store.Create(ApiKeyRole.System, "a", "A", "abc123def456");

        Assert.Equal("abc123def456", session.KeyFingerprint);
    }

    [Fact]
    public void Create_WithoutAFingerprint_LeavesItNull()
    {
        var store = Create();
        Assert.Null(store.Create(ApiKeyRole.Contribute, "a", "A").KeyFingerprint);
        Assert.Null(store.Create(ApiKeyRole.Contribute, "a", "A", "   ").KeyFingerprint);
    }

    [Fact]
    public void RevokeByFingerprint_DropsOnlySessionsFromThatKey()
    {
        var store = Create();
        var doomed = store.Create(ApiKeyRole.System, "a", "A", "key-one");
        var sameKey = store.Create(ApiKeyRole.System, "a", "A", "key-one");
        var survivor = store.Create(ApiKeyRole.Contribute, "a", "A", "key-two");

        Assert.Equal(2, store.RevokeByFingerprint("key-one"));

        Assert.False(store.TryValidate(doomed.Token, out _));
        Assert.False(store.TryValidate(sameKey.Token, out _));
        Assert.True(store.TryValidate(survivor.Token, out _));
    }

    [Fact]
    public void RevokeByFingerprint_MatchesCaseInsensitivelyAndIgnoresBlankInput()
    {
        var store = Create();
        var session = store.Create(ApiKeyRole.System, "a", "A", "ABC123");

        Assert.Equal(1, store.RevokeByFingerprint("abc123"));
        Assert.False(store.TryValidate(session.Token, out _));

        Assert.Equal(0, store.RevokeByFingerprint(""));
        Assert.Equal(0, store.RevokeByFingerprint(null!));
    }

    [Fact]
    public void RevokeByFingerprint_LeavesSessionsWithNoBoundKeyAlone()
    {
        var store = Create();
        // Sessions issued before Stage 6.3 carry no fingerprint; revoking a specific
        // key must not sweep them up, because they may belong to any key.
        var legacy = store.Create(ApiKeyRole.System, "a", "A");

        Assert.Equal(0, store.RevokeByFingerprint("some-key"));
        Assert.True(store.TryValidate(legacy.Token, out _));
    }

    /// <summary>Минимальные детерминированные часы — без зависимости ради сдвига времени.</summary>
    private sealed class FakeTimeProvider(DateTimeOffset? start = null) : TimeProvider
    {
        private DateTimeOffset _now = start ?? DateTimeOffset.UnixEpoch;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}