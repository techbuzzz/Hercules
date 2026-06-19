// Smoke test for PhraseReceiver migration. Runs against the built Hercules.dll.
// Not part of CI — invoke manually: `dotnet run scripts/test-phrase-receivers.cs`.
using System.Text.Json;
using Hercules.Storage;

int pass = 0, fail = 0;

void Check(string name, bool ok, string detail = "")
{
    if (ok) { pass++; Console.WriteLine($"  ✓ {name}"); }
    else    { fail++; Console.WriteLine($"  ✗ {name} — {detail}"); }
}

// Test 1: legacy "triggers" auto-populates PhraseReceivers on read
var jsonLegacy = """{"id":"a1","name":"Test","description":"x","triggers":["hello","hi"]}""";
var meta = JsonSerializer.Deserialize<SkillMeta>(jsonLegacy);
Check("legacy triggers → PhraseReceivers", meta!.PhraseReceivers.Count == 2 && meta.PhraseReceivers.Contains("hello"));
Check("legacy triggers serialized absent", !JsonSerializer.Serialize(meta).Contains("\"triggers\""));

// Test 2: new "phrase_receivers" works
var jsonNew = """{"id":"a2","name":"Test2","description":"x","phrase_receivers":["foo","bar"]}""";
var meta2 = JsonSerializer.Deserialize<SkillMeta>(jsonNew);
Check("new phrase_receivers → PhraseReceivers", meta2!.PhraseReceivers.Count == 2 && meta2.PhraseReceivers.Contains("foo"));

// Test 3: serialize writes only phrase_receivers (never triggers)
var meta3 = new SkillMeta { Name = "x", PhraseReceivers = ["a", "b"] };
var ser3 = JsonSerializer.Serialize(meta3);
Check("write: phrase_receivers present", ser3.Contains("phrase_receivers"));
Check("write: triggers absent", !ser3.Contains("\"triggers\""));

// Test 4: phrase_receivers takes priority when both present
var jsonBoth = """{"id":"a3","name":"x","phrase_receivers":["new1"],"triggers":["old1"]}""";
var meta4 = JsonSerializer.Deserialize<SkillMeta>(jsonBoth);
Check("priority: phrase_receivers wins", meta4!.PhraseReceivers.Count == 1 && meta4.PhraseReceivers[0] == "new1");

// Test 5: triggers used as fallback when phrase_receivers missing
var jsonTrigOnly = """{"id":"a4","name":"x","triggers":["only-old"]}""";
var meta5 = JsonSerializer.Deserialize<SkillMeta>(jsonTrigOnly);
Check("fallback: triggers populates PhraseReceivers", meta5!.PhraseReceivers.Count == 1 && meta5.PhraseReceivers[0] == "only-old");

// Test 6: empty → empty
var jsonEmpty = """{"id":"a5","name":"x"}""";
var meta6 = JsonSerializer.Deserialize<SkillMeta>(jsonEmpty);
Check("empty: empty PhraseReceivers", meta6!.PhraseReceivers.Count == 0);

Console.WriteLine();
Console.WriteLine($"=== {pass} passed, {fail} failed ===");
Environment.Exit(fail == 0 ? 0 : 1);
