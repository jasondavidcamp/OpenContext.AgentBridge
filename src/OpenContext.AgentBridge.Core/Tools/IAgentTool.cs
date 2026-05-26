namespace OpenContext.AgentBridge.Core.Tools;

public interface IAgentTool
{
    ToolDefinition Definition { get; }

    Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        AgentDirective directive,
        CancellationToken cancellationToken = default);
}
