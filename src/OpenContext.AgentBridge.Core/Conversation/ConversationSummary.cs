namespace OpenContext.AgentBridge.Core.Conversation;

public sealed record ConversationSummary(
    string Id,
    string WorkspaceRoot,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
