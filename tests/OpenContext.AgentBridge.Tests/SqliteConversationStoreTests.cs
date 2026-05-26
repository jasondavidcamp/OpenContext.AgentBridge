using OpenContext.AgentBridge.Core.Conversation;
using OpenContext.AgentBridge.Storage;

namespace OpenContext.AgentBridge.Tests;

public sealed class SqliteConversationStoreTests
{
    [Fact]
    public async Task Store_round_trips_conversation_messages()
    {
        var root = CreateTempDirectory();

        try
        {
            var store = new SqliteConversationStore(Path.Combine(root, ".agentbridge", "agentbridge.db"));
            await store.InitializeAsync();

            var conversationId = await store.CreateConversationAsync(root);
            await store.AppendMessageAsync(
                conversationId,
                new AgentMessage("user", "hello", DateTimeOffset.UtcNow));
            await store.AppendMessageAsync(
                conversationId,
                new AgentMessage("assistant", "hi there", DateTimeOffset.UtcNow));
            await store.AppendToolCallAsync(
                conversationId,
                new ToolCallRecord(
                    "read_file",
                    """{"path":"README.md"}""",
                    true,
                    "# Hello",
                    DateTimeOffset.UtcNow));

            var messages = await store.ReadMessagesAsync(conversationId);
            var summaries = await store.ListConversationsAsync(root);
            var toolCalls = await store.ReadToolCallsAsync(conversationId);

            Assert.Collection(
                messages,
                first => Assert.Equal("hello", first.Content),
                second => Assert.Equal("hi there", second.Content));

            var summary = Assert.Single(summaries);
            Assert.Equal(conversationId, summary.Id);
            Assert.Equal(root, summary.WorkspaceRoot);

            var toolCall = Assert.Single(toolCalls);
            Assert.Equal("read_file", toolCall.ToolName);
            Assert.True(toolCall.IsSuccess);
            Assert.Equal("# Hello", toolCall.ResultContent);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"agentbridge-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
