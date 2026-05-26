namespace OpenContext.AgentBridge.Core.Agents;

public enum AgentProgressKind
{
    ModelRequest,
    ModelResponse,
    ToolRequested,
    ToolCompleted,
    FinalAnswer,
    MaxIterations
}
