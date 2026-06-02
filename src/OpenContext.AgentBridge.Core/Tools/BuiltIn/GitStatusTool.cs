using OpenContext.AgentBridge.Core.Execution;

namespace OpenContext.AgentBridge.Core.Tools.BuiltIn;

public sealed class GitStatusTool : IAgentTool
{
    public ToolDefinition Definition { get; } = new(
        "git_status",
        "Show concise git working tree status for the workspace.",
        "{}");

    public async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        AgentDirective directive,
        CancellationToken cancellationToken = default)
    {
        var result = await context.Executor.RunAsync(
            context.Workspace,
            CommandRequest.Create(
                "git",
                new[] { "status", "--short", "--", ".", ":(exclude).agentbridge" },
                TimeSpan.FromSeconds(30)),
            cancellationToken);

        return result.ExitCode == 0
            ? ToolResult.Success(string.IsNullOrWhiteSpace(result.StandardOutput) ? "Working tree clean." : ToolText.Truncate(result.StandardOutput))
            : ToolResult.Failure(ToolText.Truncate(result.StandardError));
    }
}
