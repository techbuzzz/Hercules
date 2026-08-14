using Hercules.Mesh.Backends.Postgres;
using Xunit;

namespace Hercules.Agent.Tests.Mesh.Backends;

/// <summary>
///     Unit tests for <see cref="PostgresMeshConfig"/> (task_069).
/// </summary>
public class PostgresMeshConfigTests
{
    [Fact]
    public void DefaultValues_AreReasonable()
    {
        var config = new PostgresMeshConfig();

        Assert.False(config.Enabled);
        Assert.Contains("Host=localhost", config.ConnectionString);
        Assert.Equal("hercules_mesh", config.Schema);
        Assert.Equal("state", config.StateTable);
        Assert.Equal("tasks", config.TasksTable);
        Assert.Equal("tasks_dlq", config.DlqTable);
        Assert.Equal("hercules_bus_", config.ChannelPrefix);
        Assert.Equal(3600, config.DefaultTtlSeconds);
        Assert.Equal(30, config.DefaultVisibilityTimeoutSec);
        Assert.Equal(3, config.MaxDeliveryAttempts);
        Assert.True(config.UseListenNotify);
        Assert.Equal(500, config.WatchPollingIntervalMs);
        Assert.Equal(5, config.ConnectTimeoutSeconds);
        Assert.True(config.AutoCreateSchema);
        Assert.Equal(1000, config.RequeueTimerIntervalMs);
    }

    [Fact]
    public void CanSetAllProperties()
    {
        var config = new PostgresMeshConfig
        {
            Enabled = true,
            ConnectionString = "Host=db.example.com;Port=5433;Username=mesh;Password=secret;Database=mesh_db",
            Schema = "custom_mesh",
            StateTable = "kv",
            TasksTable = "jobs",
            DlqTable = "jobs_dlq",
            ChannelPrefix = "bus_",
            DefaultTtlSeconds = 7200,
            DefaultVisibilityTimeoutSec = 60,
            MaxDeliveryAttempts = 5,
            UseListenNotify = false,
            WatchPollingIntervalMs = 250,
            ConnectTimeoutSeconds = 10,
            AutoCreateSchema = false,
            RequeueTimerIntervalMs = 500
        };

        Assert.True(config.Enabled);
        Assert.Equal("Host=db.example.com;Port=5433;Username=mesh;Password=secret;Database=mesh_db", config.ConnectionString);
        Assert.Equal("custom_mesh", config.Schema);
        Assert.Equal("kv", config.StateTable);
        Assert.Equal("jobs", config.TasksTable);
        Assert.Equal("jobs_dlq", config.DlqTable);
        Assert.Equal("bus_", config.ChannelPrefix);
        Assert.Equal(7200, config.DefaultTtlSeconds);
        Assert.Equal(60, config.DefaultVisibilityTimeoutSec);
        Assert.Equal(5, config.MaxDeliveryAttempts);
        Assert.False(config.UseListenNotify);
        Assert.Equal(250, config.WatchPollingIntervalMs);
        Assert.Equal(10, config.ConnectTimeoutSeconds);
        Assert.False(config.AutoCreateSchema);
        Assert.Equal(500, config.RequeueTimerIntervalMs);
    }
}
