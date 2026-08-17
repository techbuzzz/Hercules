using Hercules.CodeExecution;
using Hercules.SkillSdk;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hercules.Agent.Tests.CodeExecutionTests;

public sealed class SkillSdkExecutorTests
{
    [Fact]
    public async Task SkillSdkExecutor_Runs_SkillSdk_EntryPoint()
    {
        var factory = new TestSkillContextFactory();
        var opts = new SandboxOptions { CpuTimeoutSeconds = 5, MaxCodeSizeKb = 1024 };
        var executor = new SkillSdkExecutor(opts, factory);

        var code = """
                   using System;
                   using Hercules.SkillSdk;

                   public class Program
                   {
                       public static void Main(IHerculesSkillContext ctx)
                       {
                           Console.WriteLine("Hello from SkillSdk");
                           Console.WriteLine($"Session: {ctx.Session.SessionId}");
                       }
                   }
                   """;

        var result = await executor.ExecuteAsync(new ExecutionRequest(code, "csharp"), CancellationToken.None);

        if (result.Status != "ok")
        {
            Assert.Fail($"Expected ok but got {result.Status}. Stderr: {result.Stderr}. Stdout: {result.Stdout}");
        }
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Hello from SkillSdk", result.Stdout);
        Assert.Contains("Session:", result.Stdout);
    }

    [Fact]
    public async Task SkillSdkExecutor_Rejects_Dangerous_Code()
    {
        var factory = new TestSkillContextFactory();
        var opts = new SandboxOptions { CpuTimeoutSeconds = 5, MaxCodeSizeKb = 1024 };
        var executor = new SkillSdkExecutor(opts, factory);

        var code = """
                   using Hercules.SkillSdk;

                   public class Program
                   {
                       public static void Main(IHerculesSkillContext ctx)
                       {
                           Process.Start("cmd.exe", "/c dir");
                       }
                   }
                   """;

        var result = await executor.ExecuteAsync(new ExecutionRequest(code, "csharp"), CancellationToken.None);

        Assert.Equal("rejected", result.Status);
    }

    [Fact]
    public async Task SkillSdkExecutor_Rejects_Reflection_Using()
    {
        var factory = new TestSkillContextFactory();
        var opts = new SandboxOptions { CpuTimeoutSeconds = 5, MaxCodeSizeKb = 1024 };
        var executor = new SkillSdkExecutor(opts, factory);

        var code = """
                   using Hercules.SkillSdk;
                   using System.Reflection;

                   public class Program
                   {
                       public static void Main(IHerculesSkillContext ctx) { }
                   }
                   """;

        var result = await executor.ExecuteAsync(new ExecutionRequest(code, "csharp"), CancellationToken.None);

        Assert.Equal("rejected", result.Status);
    }

    private sealed class TestSkillContextFactory : ISkillContextFactory
    {
        public IHerculesSkillContext Create(string sessionId, string? userId, string skillName, CancellationToken cancellationToken = default)
        {
            return new TestSkillContext(sessionId, userId, cancellationToken);
        }
    }

    private sealed class TestSkillContext : SkillContextBase
    {
        public override IHttpClient Http { get; } = new NullHttpClient();
        public override IMcpClient Mcp { get; } = new NullMcpClient();
        public override ILlmClient Llm { get; } = new NullLlmClient();
        public override IMemoryClient Memory { get; } = new NullMemoryClient();
        public override ISkillLogger Logger { get; } = new NullSkillLogger();
        public override ISessionContext Session { get; }

        public TestSkillContext(string sessionId, string? userId, CancellationToken ct)
        {
            Session = new TestSessionContext(sessionId, userId, ct);
        }
    }

    private sealed class TestSessionContext : ISessionContext
    {
        public string SessionId { get; }
        public string? UserId { get; }
        public IReadOnlyDictionary<string, string> Metadata { get; } = new Dictionary<string, string>();
        public CancellationToken CancellationToken { get; }

        public TestSessionContext(string sessionId, string? userId, CancellationToken ct)
        {
            SessionId = sessionId;
            UserId = userId;
            CancellationToken = ct;
        }
    }

    private sealed class NullHttpClient : IHttpClient
    {
        public Task<SkillHttpResponse> GetAsync(string url, CancellationToken ct = default)
            => Task.FromResult(new SkillHttpResponse { IsSuccess = true, Body = "ok" });
        public Task<SkillHttpResponse> PostAsync(string url, string? body = null, CancellationToken ct = default)
            => Task.FromResult(new SkillHttpResponse { IsSuccess = true, Body = "ok" });
        public Task<SkillHttpResponse> SendAsync(SkillHttpRequest request, CancellationToken ct = default)
            => Task.FromResult(new SkillHttpResponse { IsSuccess = true, Body = "ok" });
    }

    private sealed class NullMcpClient : IMcpClient
    {
        public Task<IReadOnlyList<string>> ListToolsAsync(string serverName, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task<SkillMcpResponse> InvokeAsync(SkillMcpRequest request, CancellationToken ct = default)
            => Task.FromResult(new SkillMcpResponse { Success = true, Output = "ok" });
    }

    private sealed class NullLlmClient : ILlmClient
    {
        public Task<SkillLlmResponse> CompleteAsync(IReadOnlyList<SkillLlmMessage> messages, CancellationToken ct = default)
            => Task.FromResult(new SkillLlmResponse { Text = "ok" });
        public Task<SkillLlmResponse> PromptAsync(string prompt, CancellationToken ct = default)
            => Task.FromResult(new SkillLlmResponse { Text = "ok" });
    }

    private sealed class NullMemoryClient : IMemoryClient
    {
        private readonly Dictionary<(string Key, SkillMemoryScope Scope), string> _store = new();

        public Task SetAsync(string key, string value, SkillMemoryScope scope = SkillMemoryScope.Execution, CancellationToken ct = default)
        {
            _store[(key, scope)] = value;
            return Task.CompletedTask;
        }

        public Task<string?> GetAsync(string key, SkillMemoryScope scope = SkillMemoryScope.Execution, CancellationToken ct = default)
        {
            _store.TryGetValue((key, scope), out var value);
            return Task.FromResult(value);
        }

        public Task<IReadOnlyList<SkillMemoryEntry>> SearchAsync(string? keyPrefix = null, string? tag = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SkillMemoryEntry>>(Array.Empty<SkillMemoryEntry>());
    }

    private sealed class NullSkillLogger : ISkillLogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}
