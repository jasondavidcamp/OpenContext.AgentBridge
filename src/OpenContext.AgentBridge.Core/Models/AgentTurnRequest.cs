using OpenContext.AgentBridge.Core.Conversation;
using OpenContext.AgentBridge.Core.Skills;

namespace OpenContext.AgentBridge.Core.Models;

public sealed record AgentTurnRequest(
    string WorkspaceRoot,
    IReadOnlyList<AgentMessage> Messages,
    IReadOnlyList<Skill> Skills);
