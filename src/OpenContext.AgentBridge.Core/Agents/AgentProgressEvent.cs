namespace OpenContext.AgentBridge.Core.Agents;

public sealed record AgentProgressEvent(
    AgentProgressKind Kind,
    int Turn,
    string Message,
    string? ToolName = null,
    string? ArgumentsJson = null,
    bool? IsSuccess = null,
    string? Preview = null);
