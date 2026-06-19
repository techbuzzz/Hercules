// Integration test for Tool ecosystem (Stage 3).
// Runs against the built Hercules.dll.
using System.Text.Json;
using Hercules.Tools;
using Hercules.Config;

int pass = 0, fail = 0;
void Check(string name, bool ok, string detail = "")
{
    if (ok) { pass++; Console.WriteLine($"  ✓ {name}"); }
    else    { fail++; Console.WriteLine($"  ✗ {name} — {detail}"); }
}

Console.WriteLine("--- ToolRegistry ---");

// Test 1: Registry with multiple tools
var httpTool = new HttpTool(new HttpConfig { AllowedDomains = ["api.github.com", "example.com"] });
var exec = new Hercules.CodeExecution.DotnetFileBasedExecutor(new Hercules.CodeExecution.SandboxOptions { SessionTtlSeconds = 0 });
var codeTool = new CodeExecutionTool(exec);
var a2a = new A2AClient(new A2AConfig());

var registry = new ToolRegistry(new ITool[] { httpTool, codeTool, a2a });
Check("registry has 3 tools", registry.Names.Count == 3);
Check("registry has http", registry.Get("http") is not null);
Check("registry has execute_code", registry.Get("execute_code") is not null);
Check("registry has a2a", registry.Get("a2a") is not null);
Check("registry get unknown = null", registry.Get("nonexistent") is null);

// Test 2: ListForLLM output
var llmList = registry.ListForLLM();
Check("ListForLLM has 'Available Tools'", llmList.Contains("Available Tools"));
Check("ListForLLM has all tool names", llmList.Contains("http") && llmList.Contains("execute_code") && llmList.Contains("a2a"));

// Test 3: Duplicate name → exception
try
{
    var dup = new ToolRegistry(new ITool[] { httpTool, httpTool });
    Check("duplicate name throws", false, "no exception");
}
catch (InvalidOperationException ex)
{
    Check("duplicate name throws", ex.Message.Contains("Duplicate"));
}

Console.WriteLine();
Console.WriteLine("--- HttpTool ---");

// Test 4: Allow-list blocks
var httpRestrictive = new HttpTool(new HttpConfig { AllowedDomains = ["api.github.com"] });
var res1 = await httpRestrictive.ExecuteAsync(JsonSerializer.Serialize(new { method = "GET", url = "https://evil.com/steal" }));
Check("allow-list blocks evil.com", !res1.Success && res1.Error!.Contains("not in the allow-list"));

// Test 5: Allow-list allows
var res2 = await httpRestrictive.ExecuteAsync(JsonSerializer.Serialize(new { method = "GET", url = "https://api.github.com/zen" }));
Check("allow-list allows api.github.com", res2.Success, $"status=false: {res2.Error}");

// Test 6: Invalid JSON
var res3 = await httpTool.ExecuteAsync("not json");
Check("invalid JSON returns error", !res3.Success && res3.Error!.Contains("Invalid"));

// Test 7: Missing method
var res4 = await httpTool.ExecuteAsync(JsonSerializer.Serialize(new { url = "https://example.com" }));
Check("missing method returns error", !res4.Success && res4.Error!.Contains("required"));

// Test 8: Wildcard subdomain match
var httpWild = new HttpTool(new HttpConfig { AllowedDomains = ["*.example.com"] });
var res5 = await httpWild.ExecuteAsync(JsonSerializer.Serialize(new { method = "GET", url = "https://api.example.com/test" }));
Check("wildcard *.example.com allows api.example.com", res5.Success, $"error={res5.Error}");
var res6 = await httpWild.ExecuteAsync(JsonSerializer.Serialize(new { method = "GET", url = "https://example.com/test" }));
Check("wildcard does not allow bare example.com", !res6.Success);

Console.WriteLine();
Console.WriteLine("--- CodeExecutionTool ---");

// Test 9: Simple code execution
var res7 = await codeTool.ExecuteAsync(JsonSerializer.Serialize(new { code = """Console.WriteLine("via tool");""" }));
Check("CodeExecutionTool hello world", res7.Success, $"error={res7.Error}, output='{res7.Output}'");

// Test 10: Invalid code rejected
var res8 = await codeTool.ExecuteAsync(JsonSerializer.Serialize(new { code = """File.Delete("C:\\Windows");""" }));
Check("CodeExecutionTool rejects File.Delete", !res8.Success && (res8.Metadata?.ContainsKey("blocked") ?? false));

// Test 11: Factorial via tool
var res9 = await codeTool.ExecuteAsync(JsonSerializer.Serialize(new
{
    code = """
    int Fact(int n) => n <= 1 ? 1 : n * Fact(n - 1);
    Console.WriteLine(Fact(8));
    """
}));
Check("CodeExecutionTool factorial(8)=40320", res9.Success && res9.Output!.Contains("40320"), $"output='{res9.Output}'");

Console.WriteLine();
Console.WriteLine("--- A2AClient ---");

// Test 12: Unknown agent
var res10 = await a2a.ExecuteAsync(JsonSerializer.Serialize(new { agent = "unknown", task = "test" }));
Check("A2A unknown agent rejected", !res10.Success && res10.Error!.Contains("Unknown agent"));

// Test 13: Missing required fields
var res11 = await a2a.ExecuteAsync(JsonSerializer.Serialize(new { agent = "x" }));
Check("A2A missing task rejected", !res11.Success);

// Test 14: A2A against non-existent endpoint (should fail with HTTP error, not crash)
var a2aLive = new A2AClient(new A2AConfig { Endpoints = new() { ["ghost"] = "http://localhost:1/never" } });
var res12 = await a2aLive.ExecuteAsync(JsonSerializer.Serialize(new { agent = "ghost", task = "test" }));
Check("A2A unreachable endpoint returns error", !res12.Success);

Console.WriteLine();
Console.WriteLine($"=== {pass} passed, {fail} failed ===");
Environment.Exit(fail == 0 ? 0 : 1);
