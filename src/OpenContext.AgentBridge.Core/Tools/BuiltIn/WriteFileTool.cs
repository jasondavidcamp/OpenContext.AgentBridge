namespace OpenContext.AgentBridge.Core.Tools.BuiltIn;

public sealed class WriteFileTool : IAgentTool
{
    public ToolDefinition Definition { get; } = new(
        "write_file",
        "Create or replace a text file inside the workspace.",
        """{"path":"relative file path","content":"complete file content","create_directories":true}""");

    public async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        AgentDirective directive,
        CancellationToken cancellationToken = default)
    {
        var path = ToolArguments.GetRequiredString(directive.Arguments, "path");
        var content = ToolArguments.GetString(directive.Arguments, "content");
        if (content is null)
        {
            return ToolResult.Failure("Missing required argument: content");
        }

        var createDirectories = ToolArguments.GetBool(directive.Arguments, "create_directories", true);
        var resolved = context.Workspace.ResolvePath(path);
        var directory = Path.GetDirectoryName(resolved);

        if (createDirectories && !string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(resolved, content, cancellationToken).ConfigureAwait(false);

        return ToolResult.Success($"Wrote {content.Length} characters to {path}.");
    }
}
