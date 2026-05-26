using OpenContext.AgentBridge.Core.Execution;

namespace OpenContext.AgentBridge.Core.Tools.BuiltIn;

public sealed class RunCommandTool : IAgentTool
{
    public ToolDefinition Definition { get; } = new(
        "run_command",
        "Run a shell command from the workspace root using the selected executor.",
        """{"command":"shell command","timeout_minutes":10}""");

    public async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        AgentDirective directive,
        CancellationToken cancellationToken = default)
    {
        var command = ToolArguments.GetRequiredString(directive.Arguments, "command");
        var timeoutMinutes = ToolArguments.GetInt(directive.Arguments, "timeout_minutes", 10, 1, 60);
        var result = await context.Executor.RunAsync(
            context.Workspace,
            ShellCommand.Create(command, context.Executor.Name, TimeSpan.FromMinutes(timeoutMinutes)),
            cancellationToken);

        var content = $"""
            Exit code: {result.ExitCode}
            Timed out: {result.TimedOut}
            Duration: {result.Duration}

            STDOUT:
            {ToolText.Truncate(result.StandardOutput)}

            STDERR:
            {ToolText.Truncate(result.StandardError)}
            """;

        return result.ExitCode == 0 && !result.TimedOut
            ? ToolResult.Success(content)
            : ToolResult.Failure(content);
    }
}
