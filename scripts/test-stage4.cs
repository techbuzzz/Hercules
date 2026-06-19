// E2E test for Stage 4: AgentCore tool-aware flow.
// Runs against the built Hercules.dll.
using System.Reflection;
using Hercules.Agent;
using Hercules.Tools;
using Hercules.Storage;
using Hercules.Config;
using Hercules.LLM;

int pass = 0, fail = 0;
void Check(string name, bool ok, string detail = "")
{
    if (ok) { pass++; Console.WriteLine($"  ✓ {name}"); }
    else    { fail++; Console.WriteLine($"  ✗ {name} — {detail}"); }
}

Console.WriteLine("--- AgentCore.TryParseAction ---");

var tryParseMethod = typeof(AgentCore).GetMethod("TryParseAction",
    BindingFlags.Static | BindingFlags.NonPublic);
if (tryParseMethod is null)
{
    Console.WriteLine("  ✗ TryParseAction method not found");
    Environment.Exit(1);
}

// Test 1: Plain JSON action
string action1 = "{\"action\": \"execute_code\", \"arguments\": {\"code\": \"Console.WriteLine(1);\"}}";
var res1 = tryParseMethod!.Invoke(null, new object?[] { action1 });
Check("plain JSON action parsed", res1 is not null);
if (res1 is ValueTuple<string, string> t1)
{
    Check("action name = execute_code", t1.Item1 == "execute_code");
    Check("arguments contains code", t1.Item2.Contains("Console.WriteLine(1)"));
}

// Test 2: Markdown-wrapped JSON
string action2 = "Вот план:\n```json\n{\"action\": \"http\", \"arguments\": {\"method\": \"GET\", \"url\": \"https://example.com\"}}\n```\nКонец.";
var res2 = tryParseMethod!.Invoke(null, new object?[] { action2 });
Check("markdown-wrapped action parsed", res2 is not null);
if (res2 is ValueTuple<string, string> t2)
{
    Check("action name = http", t2.Item1 == "http");
    Check("URL extracted", t2.Item2.Contains("example.com"));
}

// Test 3: Plain text → null
var res3 = tryParseMethod!.Invoke(null, new object?[] { "Это просто текст без action." });
Check("plain text → null", res3 is null);

// Test 4: Malformed JSON → null
var res4 = tryParseMethod!.Invoke(null, new object?[] { "{\"action\": \"x\", \"arguments\": " });
Check("malformed JSON → null", res4 is null);

// Test 5: Action without arguments → null
var res5 = tryParseMethod!.Invoke(null, new object?[] { "{\"action\": \"no_args\"}" });
Check("action without args → null", res5 is null);

Console.WriteLine();
Console.WriteLine("--- ToolRegistry injection ---");

var httpTool = new HttpTool(new HttpConfig { AllowedDomains = ["example.com"] });
var registry = new ToolRegistry(new ITool[] { httpTool });
var llmList = registry.ListForLLM();
Check("ListForLLM contains 'http'", llmList.Contains("http"));
Check("ListForLLM contains parameters schema", llmList.Contains("Parameters"));
Check("ListForLLM contains action example", llmList.Contains("\"action\""));

Console.WriteLine();
Console.WriteLine("--- Sandbox audit table ---");

var tmpDir = Path.Combine(Path.GetTempPath(), $"hercules-test-audit-{Guid.NewGuid():N}");
Directory.CreateDirectory(tmpDir);
try
{
    var store = new SqliteSessionStore(new StorageConfig
    {
        DataRoot = tmpDir,
        SqliteFile = "test.db",
    });

    store.LogSandboxExecution("sess-1", "abc123", "csharp", 0, "ok", 150, Array.Empty<string>());
    store.LogSandboxExecution("sess-1", "def456", "csharp", 1, "failed", 250, Array.Empty<string>());
    store.LogSandboxExecution("sess-1", "ghi789", "csharp", null, "rejected", 5, new[] { "File.Delete" });

    var recent = store.GetRecentSandboxExecutions(10);
    Check("audit: 3 entries logged", recent.Count == 3);
    Check("audit: most recent is rejected", recent[0].Status == "rejected");
    Check("audit: blocked patterns stored", recent[0].BlockedPatterns.Contains("File.Delete"));

    var failureRate = store.GetRecentSandboxFailureRate(5);
    Check("audit: failure rate > 0.5 (1 ok + 2 failed/rejected)", failureRate > 0.5, $"got {failureRate}");

    store.Dispose();
}
finally
{
    try { Directory.Delete(tmpDir, recursive: true); } catch { }
}

Console.WriteLine();
Console.WriteLine($"=== {pass} passed, {fail} failed ===");
Environment.Exit(fail == 0 ? 0 : 1);
