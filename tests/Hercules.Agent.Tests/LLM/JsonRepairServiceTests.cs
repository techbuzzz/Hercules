using Hercules.Contracts;
using Hercules.LLM.JsonRepair;
using Xunit;

namespace Hercules.Agent.Tests.LLM;

public class JsonRepairServiceTests
{
    private readonly JsonRepairService _svc = new();

    // ========== TryParse<T> — happy paths ==========

    [Fact]
    public void TryParse_CleanJson_ReturnsInstance()
    {
        var json = """{"action": "http_fetch", "arguments": {"url": "https://example.com"}, "version": "1.0"}""";
        var result = _svc.TryParse<ToolCallContract>(json);
        Assert.NotNull(result);
        Assert.Equal("http_fetch", result.Action);
        Assert.Equal("1.0", result.Version);
        Assert.Single(result.Arguments);
    }

    [Fact]
    public void TryParse_MarkdownWrapped_ReturnsInstance()
    {
        var text = """
            Sure, here is the JSON:
            ```json
            {"action": "code_exec", "arguments": {"lang": "python", "code": "print(1)"}, "version": "1.0"}
            ```
            """;
        var result = _svc.TryParse<ToolCallContract>(text);
        Assert.NotNull(result);
        Assert.Equal("code_exec", result.Action);
    }

    [Fact]
    public void TryParse_PlainMarkdown_ReturnsInstance()
    {
        var text = """
            ```{"action": "read_file", "arguments": {}, "version": "1.0"}```
            """;
        var result = _svc.TryParse<ToolCallContract>(text);
        Assert.NotNull(result);
        Assert.Equal("read_file", result.Action);
    }

    [Fact]
    public void TryParse_ExtraTextBeforeAfter_ReturnsInstance()
    {
        var text = "ERROR: Something went wrong but here is what I decided:\n" +
                   "{\"action\": \"test\", \"arguments\": {}, \"version\": \"1.0\"}\n" +
                   "Let me know if you need anything else!";
        var result = _svc.TryParse<ToolCallContract>(text);
        Assert.NotNull(result);
        Assert.Equal("test", result.Action);
    }

    [Fact]
    public void TryParse_TrailingComma_Repaired()
    {
        var json = """{"action": "x", "arguments": {}, "version": "1.0",}""";
        var result = _svc.TryParse<ToolCallContract>(json);
        Assert.NotNull(result);
        Assert.Equal("x", result.Action);
    }

    [Fact]
    public void TryParse_CommentBeforeJson_Stripped()
    {
        var text = "Sure, here's the result:\n" +
                   """{"action": "my_tool", "arguments": {}, "version": "1.0"}""";
        var result = _svc.TryParse<ToolCallContract>(text);
        Assert.NotNull(result);
        Assert.Equal("my_tool", result.Action);
    }

    [Fact]
    public void TryParse_InvalidJson_ReturnsNull()
    {
        var text = "This is not JSON at all, just plain text from LLM.";
        var result = _svc.TryParse<ToolCallContract>(text);
        Assert.Null(result);
    }

    [Fact]
    public void TryParse_PartialJson_ReturnsNull()
    {
        var text = """{"action": "tool", "argu""";
        var result = _svc.TryParse<ToolCallContract>(text);
        Assert.Null(result);
    }

    [Fact]
    public void TryParse_SkillCreation_ReturnsTypedContract()
    {
        var json = """
            ```json
            {
              "name": "Code Review",
              "description": "Reviews code and suggests improvements",
              "phrase_receivers": ["проверь код", "review code"],
              "prompt": "You are a code reviewer. ..."
            }
            ```
            """;
        var result = _svc.TryParse<SkillCreationContract>(json);
        Assert.NotNull(result);
        Assert.Equal("Code Review", result.Name);
        Assert.Equal(2, result.PhraseReceivers.Count);
    }

    [Fact]
    public void TryParse_SkillImprove_ReturnsTypedContract()
    {
        var json = """{"description": "Improved description", "prompt": "Improved prompt", "version": "1.0"}""";
        var result = _svc.TryParse<SkillImproveContract>(json);
        Assert.NotNull(result);
        Assert.Equal("Improved description", result.Description);
        Assert.Equal("Improved prompt", result.Prompt);
    }

    [Fact]
    public void TryParse_EmptyString_ReturnsNull()
    {
        var result = _svc.TryParse<ToolCallContract>("");
        Assert.Null(result);
    }

    [Fact]
    public void TryParse_WhitespaceOnly_ReturnsNull()
    {
        var result = _svc.TryParse<ToolCallContract>("   \n\t  ");
        Assert.Null(result);
    }

    [Fact]
    public void TryParse_ArrayJson_ReturnsNull_ForObjectContract()
    {
        var json = """[{"a": 1}, {"b": 2}]""";
        var result = _svc.TryParse<ToolCallContract>(json);
        Assert.Null(result);
    }

    [Fact]
    public void TryParse_ToolResultContract_DeserializesCorrectly()
    {
        var json = """
            {"success": true, "output": "42", "error": null, "version": "1.0", "request_id": "req-123"}
            """;
        var result = _svc.TryParse<ToolResultContract>(json);
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal("42", result.Output);
        Assert.Equal("req-123", result.RequestId);
    }

    [Fact]
    public void TryParse_AgentResponseContract_DeserializesCorrectly()
    {
        var json = """
            {"answer": "Привет!", "mode": "skill", "confidence": "high", "version": "1.0"}
            """;
        var result = _svc.TryParse<AgentResponseContract>(json);
        Assert.NotNull(result);
        Assert.Equal("Привет!", result.Answer);
        Assert.Equal("skill", result.Mode);
        Assert.Equal("high", result.Confidence);
    }

    // ========== ExtractJson ==========

    [Fact]
    public void ExtractJson_MarkdownBlock_Extracts()
    {
        var text = "Here is the JSON:\n```json\n{\"a\": 1}\n```\nThat's all.";
        var result = _svc.ExtractJson(text);
        Assert.Equal("{\"a\": 1}", result);
    }

    [Fact]
    public void ExtractJson_PlainBlock_Extracts()
    {
        var text = "```\n{\"key\": \"value\"}\n```";
        var result = _svc.ExtractJson(text);
        Assert.Equal("{\"key\": \"value\"}", result);
    }

    [Fact]
    public void ExtractJson_RawJson_Extracts()
    {
        var text = "Some text before\n{\"x\": 123}\nSome text after";
        var result = _svc.ExtractJson(text);
        Assert.Equal("{\"x\": 123}", result);
    }

    [Fact]
    public void ExtractJson_ArrayJson_Extracts()
    {
        var text = "Here is the array: [1, 2, 3]";
        var result = _svc.ExtractJson(text);
        Assert.Equal("[1, 2, 3]", result);
    }

    [Fact]
    public void ExtractJson_NoJson_ReturnsNull()
    {
        var result = _svc.ExtractJson("Just plain text without JSON");
        Assert.Null(result);
    }

    // ========== Repair ==========

    [Fact]
    public void Repair_TrailingComma_Removes()
    {
        var result = _svc.Repair("""{"a": 1, "b": 2,}""");
        Assert.Equal("""{"a": 1, "b": 2}""", result);
    }

    [Fact]
    public void Repair_TrailingCommaInArray_Removes()
    {
        var result = _svc.Repair("""[1, 2, 3,]""");
        Assert.Equal("[1, 2, 3]", result);
    }

    [Fact]
    public void Repair_TrimsText_BeforeFirstBrace()
    {
        var result = _svc.Repair("text before {\"a\": 1} text after");
        Assert.StartsWith("{", result);
    }

    [Fact]
    public void Repair_TrimsText_AfterLastBrace()
    {
        var result = _svc.Repair("text before {\"a\": 1} text after");
        Assert.EndsWith("}", result);
    }

    [Fact]
    public void Repair_NestedObjects_Preserves()
    {
        var json = """{"outer": {"inner": 1}, "arr": [{"x": 2}]}""";
        var result = _svc.Repair(json);
        Assert.Equal(json, result);
    }

    [Fact]
    public void Repair_AlreadyValid_RemainsUnchanged()
    {
        var json = """{"action": "x", "version": "1.0"}""";
        var result = _svc.Repair(json);
        Assert.Equal(json, result);
    }
}
