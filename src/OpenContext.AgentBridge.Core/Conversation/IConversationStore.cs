namespace OpenContext.AgentBridge.Core.Conversation;

public interface IConversationStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<string> CreateConversationAsync(
        string workspaceRoot,
        CancellationToken cancellationToken = default);

    Task AppendMessageAsync(
        string conversationId,
        AgentMessage message,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentMessage>> ReadMessagesAsync(
        string conversationId,
        CancellationToken cancellationToken = default);

    Task AppendToolCallAsync(
        string conversationId,
        ToolCallRecord toolCall,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ToolCallRecord>> ReadToolCallsAsync(
        string conversationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ConversationSummary>> ListConversationsAsync(
        string workspaceRoot,
        CancellationToken cancellationToken = default);
}
