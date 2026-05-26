namespace OpenContext.AgentBridge.Core.Conversation;

public sealed record AgentMessage(
    string Role,
    string Content,
    DateTimeOffset CreatedAt);
