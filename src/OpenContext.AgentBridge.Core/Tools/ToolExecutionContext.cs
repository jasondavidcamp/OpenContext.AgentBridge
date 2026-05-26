using OpenContext.AgentBridge.Core.Execution;
using OpenContext.AgentBridge.Core.Workspaces;

namespace OpenContext.AgentBridge.Core.Tools;

public sealed record ToolExecutionContext(
    WorkspaceContext Workspace,
    IWorkspaceExecutor Executor);
