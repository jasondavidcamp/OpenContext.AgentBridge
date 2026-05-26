using OpenContext.AgentBridge.Core.Conversation;

namespace OpenContext.AgentBridge.Core.Agents;

public sealed record AgentLoopResult(
    string FinalMessage,
    int Turns,
    StoppedBecause StoppedBecause,
    IReadOnlyList<ToolCallRecord> ToolCalls);
