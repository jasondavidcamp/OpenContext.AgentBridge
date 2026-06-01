using OpenContext.AgentBridge.Core.Conversation;
using OpenContext.AgentBridge.Core.Skills;
using OpenContext.AgentBridge.Core.Tools;
using OpenContext.AgentBridge.Core.Workspaces;

namespace OpenContext.AgentBridge.Core.Models;

public sealed record AgentTurnRequest(
    string WorkspaceRoot,
    IReadOnlyList<AgentMessage> Messages,
    IReadOnlyList<Skill> Skills,
    IReadOnlyList<ToolDefinition> Tools,
    WorkspaceMap? WorkspaceMap = null);
