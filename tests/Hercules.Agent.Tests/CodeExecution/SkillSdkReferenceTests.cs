using Hercules.CodeExecution;
using Xunit;

namespace Hercules.Agent.Tests.CodeExecutionTests;

public sealed class SkillSdkReferenceTests
{
    [Fact]
    public void FindNetCoreAppRefDirectory_Locates_Reference_Assemblies()
    {
        var refDir = SkillSdkExecutor.FindNetCoreAppRefDirectory();
        Assert.NotNull(refDir);
        Assert.True(Directory.Exists(refDir));
        Assert.True(File.Exists(Path.Combine(refDir, "System.Console.dll")));
        Assert.True(File.Exists(Path.Combine(refDir, "System.Runtime.dll")));
        Assert.True(File.Exists(Path.Combine(refDir, "netstandard.dll")));
    }
}
