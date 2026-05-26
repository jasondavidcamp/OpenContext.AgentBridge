using OpenContext.AgentBridge.Core.Agents;
using OpenContext.AgentBridge.Core.Conversation;
using OpenContext.AgentBridge.Core.Execution;
using OpenContext.AgentBridge.Core.Models;
using OpenContext.AgentBridge.Core.Tools;
using OpenContext.AgentBridge.Core.Tools.BuiltIn;
using OpenContext.AgentBridge.Core.Workspaces;
using OpenContext.AgentBridge.Storage;

namespace OpenContext.AgentBridge.Tests;

public sealed class AgentLoopTests
{
    [Fact]
    public async Task RunAsync_executes_tool_action_and_returns_final_answer()
    {
        var root = CreateTempDirectory();

        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "README.md"), "# AgentBridge");

            var workspace = WorkspaceContext.FromPath(root);
            workspace.EnsureLocalState();

            var store = new SqliteConversationStore(workspace.ConversationDatabasePath);
            await store.InitializeAsync();
            var conversationId = await store.CreateConversationAsync(workspace.RootPath);
            await store.AppendMessageAsync(
                conversationId,
                new AgentMessage("user", "Read the README.", DateTimeOffset.UtcNow));

            var provider = new QueueModelProvider(
                """{"type":"tool","tool":"read_file","arguments":{"path":"README.md"}}""",
                """{"type":"final","message":"README says AgentBridge."}""");
            var loop = new AgentLoop(
                provider,
                store,
                new ToolRegistry(BuiltInTools.CreateDefault()));
            var progress = new ListProgress();

            var result = await loop.RunAsync(
                conversationId,
                workspace,
                new HostWorkspaceExecutor(),
                Array.Empty<OpenContext.AgentBridge.Core.Skills.Skill>(),
                new AgentLoopOptions(8, progress));

            var toolCalls = await store.ReadToolCallsAsync(conversationId);
            var messages = await store.ReadMessagesAsync(conversationId);

            Assert.Equal("README says AgentBridge.", result.FinalMessage);
            var toolCall = Assert.Single(toolCalls);
            Assert.Equal("read_file", toolCall.ToolName);
            Assert.True(toolCall.IsSuccess);
            Assert.Contains("AgentBridge", toolCall.ResultContent);
            var resultToolCall = Assert.Single(result.ToolCalls);
            Assert.Equal("read_file", resultToolCall.ToolName);
            Assert.Contains(messages, message => message.Content.StartsWith("TOOL_RESULT", StringComparison.Ordinal));
            Assert.Contains(progress.Events, value => value.Kind == AgentProgressKind.ToolRequested && value.ToolName == "read_file");
            Assert.Contains(progress.Events, value => value.Kind == AgentProgressKind.ToolCompleted && value.IsSuccess == true);
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

    [Fact]
    public async Task RunAsync_recovers_from_invalid_model_response()
    {
        var root = CreateTempDirectory();

        try
        {
            var workspace = WorkspaceContext.FromPath(root);
            workspace.EnsureLocalState();

            var store = new SqliteConversationStore(workspace.ConversationDatabasePath);
            await store.InitializeAsync();
            var conversationId = await store.CreateConversationAsync(workspace.RootPath);
            await store.AppendMessageAsync(
                conversationId,
                new AgentMessage("user", "Say done.", DateTimeOffset.UtcNow));

            var provider = new QueueModelProvider(
                "Sure, I can do that.",
                """{"type":"final","message":"Done."}""");
            var loop = new AgentLoop(
                provider,
                store,
                new ToolRegistry(BuiltInTools.CreateDefault()));
            var progress = new ListProgress();

            var result = await loop.RunAsync(
                conversationId,
                workspace,
                new HostWorkspaceExecutor(),
                Array.Empty<OpenContext.AgentBridge.Core.Skills.Skill>(),
                new AgentLoopOptions(8, progress));

            var messages = await store.ReadMessagesAsync(conversationId);

            Assert.Equal("Done.", result.FinalMessage);
            Assert.Equal(2, result.Turns);
            Assert.Contains(messages, message => message.Content.StartsWith("MODEL_RESPONSE_PARSE_ERROR", StringComparison.Ordinal));
            Assert.Contains(progress.Events, value => value.Kind == AgentProgressKind.InvalidModelResponse);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }


    private sealed class ListProgress : IProgress<AgentProgressEvent>
    {
        public List<AgentProgressEvent> Events { get; } = new();

        public void Report(AgentProgressEvent value)
        {
            Events.Add(value);
        }
    }

    private sealed class QueueModelProvider : IModelProvider
    {
        private readonly Queue<string> _responses;

        public QueueModelProvider(params string[] responses)
        {
            _responses = new Queue<string>(responses);
        }

        public string Name => "queue";

        public Task<AgentTurnResponse> CompleteAsync(
            AgentTurnRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentTurnResponse(_responses.Dequeue()));
        }
    }
}
