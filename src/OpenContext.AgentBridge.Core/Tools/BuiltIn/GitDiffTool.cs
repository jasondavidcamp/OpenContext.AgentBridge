using OpenContext.AgentBridge.Core.Execution;

namespace OpenContext.AgentBridge.Core.Tools.BuiltIn;

public sealed class GitDiffTool : IAgentTool
{
    public ToolDefinition Definition { get; } = new(
        "git_diff",
        "Show the current git diff for the workspace, optionally limited to one path.",
        """{"path":"optional relative path"}""");

    public async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        AgentDirective directive,
        CancellationToken cancellationToken = default)
    {
        var path = ToolArguments.GetString(directive.Arguments, "path");
        var arguments = new List<string> { "diff", "--" };

        if (!string.IsNullOrWhiteSpace(path))
        {
            var resolved = context.Workspace.ResolvePath(path);
            arguments.Add(ToToolPath(Path.GetRelativePath(context.Workspace.RootPath, resolved)));
        }

        var result = await context.Executor.RunAsync(
            context.Workspace,
            CommandRequest.Create("git", arguments, TimeSpan.FromSeconds(30)),
            cancellationToken);

        return result.ExitCode == 0
            ? ToolResult.Success(string.IsNullOrWhiteSpace(result.StandardOutput) ? "No diff." : ToolText.Truncate(result.StandardOutput))
            : ToolResult.Failure(ToolText.Truncate(result.StandardError));
    }

    private static string ToToolPath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/');
    }
}
