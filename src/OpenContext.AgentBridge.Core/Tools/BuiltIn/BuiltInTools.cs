namespace OpenContext.AgentBridge.Core.Tools.BuiltIn;

public static class BuiltInTools
{
    public static IReadOnlyList<IAgentTool> CreateDefault()
    {
        return new IAgentTool[]
        {
            new GitDiffTool(),
            new GitStatusTool(),
            new ListFilesTool(),
            new ReadFileTool(),
            new RunCommandTool(),
            new SearchTool(),
            new WriteFileTool()
        };
    }
}
