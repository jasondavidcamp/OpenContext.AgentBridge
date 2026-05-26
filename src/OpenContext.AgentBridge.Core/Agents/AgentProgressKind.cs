namespace OpenContext.AgentBridge.Core.Agents;

public enum AgentProgressKind
{
    ModelRequest,
    ModelResponse,
    InvalidModelResponse,
    ToolRequested,
    ToolCompleted,
    FinalAnswer,
    MaxIterations
}
