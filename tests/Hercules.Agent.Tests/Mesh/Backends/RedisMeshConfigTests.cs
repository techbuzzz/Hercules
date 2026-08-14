using Hercules.Mesh.Backends.Redis;
using Xunit;

namespace Hercules.Agent.Tests.Mesh.Backends;

/// <summary>
///     Unit tests for <see cref="RedisMeshConfig"/> (task_067).
/// </summary>
public class RedisMeshConfigTests
{
    [Fact]
    public void DefaultValues_AreReasonable()
    {
        var config = new RedisMeshConfig();

        Assert.False(config.Enabled);
        Assert.Equal("localhost:6379,abortConnect=false,connectTimeout=5000,syncTimeout=5000", config.ConnectionString);
        Assert.Equal("hercules:mesh:", config.KeyPrefix);
        Assert.Equal("hercules:bus:", config.ChannelPrefix);
        Assert.Equal(3600, config.DefaultTtlSeconds);
        Assert.Equal(30, config.DefaultVisibilityTimeoutSec);
        Assert.True(config.UseKeyspaceNotifications);
        Assert.Equal(500, config.WatchPollingIntervalMs);
    }

    [Fact]
    public void CanSetAllProperties()
    {
        var config = new RedisMeshConfig
        {
            Enabled = true,
            ConnectionString = "redis.example.com:6380,password=secret",
            KeyPrefix = "myapp:",
            ChannelPrefix = "mybus:",
            DefaultTtlSeconds = 7200,
            DefaultVisibilityTimeoutSec = 60,
            UseKeyspaceNotifications = false,
            WatchPollingIntervalMs = 200
        };

        Assert.True(config.Enabled);
        Assert.Equal("redis.example.com:6380,password=secret", config.ConnectionString);
        Assert.Equal("myapp:", config.KeyPrefix);
        Assert.Equal("mybus:", config.ChannelPrefix);
        Assert.Equal(7200, config.DefaultTtlSeconds);
        Assert.Equal(60, config.DefaultVisibilityTimeoutSec);
        Assert.False(config.UseKeyspaceNotifications);
        Assert.Equal(200, config.WatchPollingIntervalMs);
    }
}
