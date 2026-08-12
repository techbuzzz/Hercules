using Hercules.Memory.Layers;
using Xunit;

namespace Hercules.Agent.Tests.Memory.Layers;

public class WorkingMemoryServiceTests
{
    [Fact]
    public void Constructor_DefaultConfig_SetsMaxEntries()
    {
        var service = new WorkingMemoryService();

        Assert.Equal(0, service.Count); // empty initially
    }

    [Fact]
    public void Set_SingleEntry_IncreasesCount()
    {
        var service = new WorkingMemoryService();

        service.Set("key", "value");

        Assert.Equal(1, service.Count);
    }

    [Fact]
    public void Set_OverwritesExisting_DoesNotIncreaseCount()
    {
        var service = new WorkingMemoryService();

        service.Set("key", "value1");
        service.Set("key", "value2");

        Assert.Equal(1, service.Count);
        Assert.Equal("value2", service.Get("key"));
    }

    [Fact]
    public void GetAll_ReturnsAllEntries()
    {
        var service = new WorkingMemoryService();
        service.Set("key1", "val1");
        service.Set("key2", "val2");

        var all = service.GetAll();

        Assert.Equal(2, all.Count);
        Assert.Equal("val1", all["key1"].Value);
        Assert.Equal("val2", all["key2"].Value);
    }

    [Fact]
    public void Clear_ResetsCount()
    {
        var service = new WorkingMemoryService();
        service.Set("key1", "val1");
        service.Set("key2", "val2");

        service.Clear();

        Assert.Equal(0, service.Count);
    }

    [Fact]
    public void WorkingMemoryEntry_MetadataPreserved()
    {
        var service = new WorkingMemoryService();
        var entry = new MemoryEntry("test_source", MemoryConfidence.High, 0, MemorySensitivity.Sensitive, DateTime.UtcNow, new List<string> { "tag1" });
        service.Set("key", "value", entry);

        var all = service.GetAll();

        Assert.Single(all);
        Assert.Equal("test_source", all["key"].Entry.Source);
        Assert.Equal(MemoryConfidence.High, all["key"].Entry.Confidence);
        Assert.Equal(MemorySensitivity.Sensitive, all["key"].Entry.Sensitivity);
    }

    [Fact]
    public void Clear_AfterDispose_AllowsReuse()
    {
        var service = new WorkingMemoryService();
        service.Set("key", "value");
        service.Clear();
        service.Set("newkey", "newvalue");

        Assert.Equal(1, service.Count);
        Assert.Equal("newvalue", service.Get("newkey"));
    }

    [Fact]
    public void MultipleInstances_AreIsolated()
    {
        var service1 = new WorkingMemoryService();
        var service2 = new WorkingMemoryService();

        service1.Set("key", "from_service1");
        service2.Set("key", "from_service2");

        Assert.Equal("from_service1", service1.Get("key"));
        Assert.Equal("from_service2", service2.Get("key"));
        Assert.Equal(1, service1.Count);
        Assert.Equal(1, service2.Count);
    }
}
