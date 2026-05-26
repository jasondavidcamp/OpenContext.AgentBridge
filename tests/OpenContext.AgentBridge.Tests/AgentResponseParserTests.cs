using OpenContext.AgentBridge.Core.Tools;

namespace OpenContext.AgentBridge.Tests;

public sealed class AgentResponseParserTests
{
    [Fact]
    public void TryParse_parses_tool_action()
    {
        var parsed = AgentResponseParser.TryParse(
            """{"type":"tool","tool":"read_file","arguments":{"path":"README.md"}}""",
            out var directive);

        Assert.True(parsed);
        Assert.Equal("tool", directive.Type);
        Assert.Equal("read_file", directive.ToolName);
        Assert.Equal("README.md", directive.Arguments["path"]?.GetValue<string>());
    }

    [Fact]
    public void TryParse_parses_fenced_final_answer()
    {
        var parsed = AgentResponseParser.TryParse(
            """
            ```json
            {"type":"final","message":"Done."}
            ```
            """,
            out var directive);

        Assert.True(parsed);
        Assert.Equal("final", directive.Type);
        Assert.Equal("Done.", directive.Message);
    }

    [Fact]
    public void TryParse_parses_json_inside_prose_and_ignores_other_braces()
    {
        var parsed = AgentResponseParser.TryParse(
            """
            I will use this shape: {not json}.

            ```json
            {"type":"tool","tool":"search","arguments":{"query":"class AgentLoop"}}
            ```
            """,
            out var directive);

        Assert.True(parsed);
        Assert.Equal("tool", directive.Type);
        Assert.Equal("search", directive.ToolName);
        Assert.Equal("class AgentLoop", directive.Arguments["query"]?.GetValue<string>());
    }

    [Fact]
    public void TryParse_returns_false_when_no_valid_directive_exists()
    {
        var parsed = AgentResponseParser.TryParse(
            "I can help with that.",
            out _);

        Assert.False(parsed);
    }
}
