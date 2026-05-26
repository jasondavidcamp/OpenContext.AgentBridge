namespace OpenContext.AgentBridge.Core.Tools.BuiltIn;

public sealed class ReadFileTool : IAgentTool
{
    public ToolDefinition Definition { get; } = new(
        "read_file",
        "Read a UTF-8/text file from inside the workspace.",
        """{"path":"relative file path","max_chars":12000}""");

    public async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        AgentDirective directive,
        CancellationToken cancellationToken = default)
    {
        var path = ToolArguments.GetRequiredString(directive.Arguments, "path");
        var maxCharacters = ToolArguments.GetInt(directive.Arguments, "max_chars", 12_000, 1, 50_000);
        var resolved = context.Workspace.ResolvePath(path);

        if (!File.Exists(resolved))
        {
            return ToolResult.Failure($"File does not exist: {path}");
        }

        var content = await File.ReadAllTextAsync(resolved, cancellationToken).ConfigureAwait(false);
        return ToolResult.Success(ToolText.Truncate(content, maxCharacters));
    }
}
