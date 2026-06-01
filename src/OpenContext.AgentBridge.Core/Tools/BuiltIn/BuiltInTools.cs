namespace OpenContext.AgentBridge.Core.Tools.BuiltIn;

public static class BuiltInTools
{
    public static IReadOnlyList<IAgentTool> CreateDefault()
    {
        return new IAgentTool[]
        {
            new ListFilesTool(),
            new ReadFileTool(),
            new ReplaceTextTool(),
            new SearchTool(),
            new ApplyPatchTool(),
            new WriteFileTool(),
            new RunCommandTool(),
            new GitStatusTool(),
            new GitDiffTool()
        };
    }
}
