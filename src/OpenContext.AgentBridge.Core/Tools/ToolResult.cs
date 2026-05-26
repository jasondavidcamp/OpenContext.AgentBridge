namespace OpenContext.AgentBridge.Core.Tools;

public sealed record ToolResult(
    bool IsSuccess,
    string Content)
{
    public static ToolResult Success(string content)
    {
        return new ToolResult(true, content);
    }

    public static ToolResult Failure(string content)
    {
        return new ToolResult(false, content);
    }
}
