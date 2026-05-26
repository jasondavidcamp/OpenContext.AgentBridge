namespace OpenContext.AgentBridge.Core.Conversation;

public sealed record ToolCallRecord(
    string ToolName,
    string ArgumentsJson,
    bool IsSuccess,
    string ResultContent,
    DateTimeOffset CreatedAt);
