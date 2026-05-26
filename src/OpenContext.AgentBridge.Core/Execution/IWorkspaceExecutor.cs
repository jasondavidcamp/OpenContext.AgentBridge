using OpenContext.AgentBridge.Core.Workspaces;

namespace OpenContext.AgentBridge.Core.Execution;

public interface IWorkspaceExecutor
{
    string Name { get; }

    Task<CommandResult> RunAsync(
        WorkspaceContext workspace,
        CommandRequest request,
        CancellationToken cancellationToken = default);
}
