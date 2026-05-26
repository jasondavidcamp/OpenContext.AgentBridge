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
}
