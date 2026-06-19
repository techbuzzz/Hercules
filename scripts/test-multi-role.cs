// Smoke test for multi-role routing. Runs against the built Hercules.dll.
// Not part of CI — invoke manually: `dotnet run scripts/test-multi-role.cs`.
using Hercules.Config;
using Hercules.LLM;

int pass = 0, fail = 0;
void Check(string name, bool ok, string detail = "")
{
    if (ok) { pass++; Console.WriteLine($"  ✓ {name}"); }
    else    { fail++; Console.WriteLine($"  ✗ {name} — {detail}"); }
}

// Test 1: Roles static constants
Check("Roles.Main == \"main\"", Roles.Main == "main");
Check("Roles.CodeWriter == \"code_writer\"", Roles.CodeWriter == "code_writer");
Check("Roles.Reflector == \"reflector\"", Roles.Reflector == "reflector");

// Test 2: AppConfig.Roles default
var cfg = new AppConfig();
Check("AppConfig.Roles initialized", cfg.Roles is not null);
Check("AppConfig.Roles empty by default", cfg.Roles.Count == 0);

// Test 3: RoleConfig defaults
var rc = new RoleConfig();
Check("RoleConfig.Provider empty default", rc.Provider == "");
Check("RoleConfig.Model empty default", rc.Model == "");
Check("RoleConfig.Temperature default 0.6", Math.Abs(rc.Temperature - 0.6f) < 0.01f);

// Test 4: RoleConfig with values
rc.Provider = "ollama-local";
rc.Model = "gemma2:2b";
rc.Temperature = 0.1f;
cfg.Roles["code_writer"] = rc;
Check("Roles[name] lookup", cfg.Roles["code_writer"].Model == "gemma2:2b");
Check("Roles count after add", cfg.Roles.Count == 1);

Console.WriteLine();
Console.WriteLine($"=== {pass} passed, {fail} failed ===");
Environment.Exit(fail == 0 ? 0 : 1);
