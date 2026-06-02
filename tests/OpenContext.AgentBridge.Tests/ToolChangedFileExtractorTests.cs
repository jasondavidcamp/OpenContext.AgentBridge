using OpenContext.AgentBridge.Core.Conversation;
using OpenContext.AgentBridge.Core.Tools;

namespace OpenContext.AgentBridge.Tests;

public sealed class ToolChangedFileExtractorTests
{
    [Fact]
    public void Extract_returns_paths_from_successful_mutating_tools()
    {
        var toolCalls = new[]
        {
            ToolCall("read_file", """{"path":"README.md"}""", isSuccess: true),
            ToolCall("replace_text", """{"path":"scripts\\Get-Greeting.ps1"}""", isSuccess: true),
            ToolCall("write_file", """{"path":"scripts/Get-Greeting.ps1"}""", isSuccess: true),
            ToolCall("write_file", """{"path":"notes.md"}""", isSuccess: false)
        };

        var changedFiles = ToolChangedFileExtractor.Extract(toolCalls);

        Assert.Equal(new[] { "scripts/Get-Greeting.ps1" }, changedFiles);
    }

    [Fact]
    public void Extract_returns_patch_paths_when_patch_was_applied()
    {
        var patch = """
            diff --git a/Get-Greeting.ps1 b/Get-Greeting.ps1
            --- a/Get-Greeting.ps1
            +++ b/Get-Greeting.ps1
            @@ -1 +1 @@
            -Hello
            +Hello from AgentBridge
            """;
        var toolCalls = new[]
        {
            ToolCall("apply_patch", $$"""{"patch":{{JsonString(patch)}},"check_only":false}""", isSuccess: true)
        };

        var changedFiles = ToolChangedFileExtractor.Extract(toolCalls);

        Assert.Equal(new[] { "Get-Greeting.ps1" }, changedFiles);
    }

    [Fact]
    public void Extract_ignores_patch_check_only_and_unsafe_paths()
    {
        var patch = """
            diff --git a/../outside.txt b/../outside.txt
            --- a/../outside.txt
            +++ b/../outside.txt
            @@ -1 +1 @@
            -old
            +new
            """;
        var toolCalls = new[]
        {
            ToolCall("apply_patch", $$"""{"patch":{{JsonString(patch)}},"check_only":true}""", isSuccess: true),
            ToolCall("write_file", """{"path":"../outside.txt"}""", isSuccess: true)
        };

        var changedFiles = ToolChangedFileExtractor.Extract(toolCalls);

        Assert.Empty(changedFiles);
    }

    private static ToolCallRecord ToolCall(string toolName, string argumentsJson, bool isSuccess)
    {
        return new ToolCallRecord(toolName, argumentsJson, isSuccess, "ok", DateTimeOffset.UtcNow);
    }

    private static string JsonString(string value)
    {
        return System.Text.Json.JsonSerializer.Serialize(value);
    }
}
