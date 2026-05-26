using OpenContext.AgentBridge.Core.Conversation;
using OpenContext.AgentBridge.Core.Execution;
using OpenContext.AgentBridge.Core.Models;
using OpenContext.AgentBridge.Core.Skills;
using OpenContext.AgentBridge.Core.Tools;
using OpenContext.AgentBridge.Core.Workspaces;

namespace OpenContext.AgentBridge.Core.Agents;

public sealed class AgentLoop
{
    private readonly IModelProvider _modelProvider;
    private readonly IConversationStore _conversationStore;
    private readonly ToolRegistry _toolRegistry;

    public AgentLoop(
        IModelProvider modelProvider,
        IConversationStore conversationStore,
        ToolRegistry toolRegistry)
    {
        _modelProvider = modelProvider;
        _conversationStore = conversationStore;
        _toolRegistry = toolRegistry;
    }

    public async Task<AgentLoopResult> RunAsync(
        string conversationId,
        WorkspaceContext workspace,
        IWorkspaceExecutor executor,
        IReadOnlyList<Skill> skills,
        AgentLoopOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new AgentLoopOptions();
        var context = new ToolExecutionContext(workspace, executor);

        for (var turn = 1; turn <= options.MaxToolIterations; turn++)
        {
            var messages = await _conversationStore
                .ReadMessagesAsync(conversationId, cancellationToken)
                .ConfigureAwait(false);

            var response = await _modelProvider
                .CompleteAsync(
                    new AgentTurnRequest(
                        workspace.RootPath,
                        messages,
                        skills,
                        _toolRegistry.Definitions),
                    cancellationToken)
                .ConfigureAwait(false);

            await _conversationStore
                .AppendMessageAsync(
                    conversationId,
                    new AgentMessage("assistant", response.Content, DateTimeOffset.UtcNow),
                    cancellationToken)
                .ConfigureAwait(false);

            if (!AgentResponseParser.TryParse(response.Content, out var directive))
            {
                return new AgentLoopResult(response.Content, turn, StoppedBecause.FinalAnswer);
            }

            if (string.Equals(directive.Type, "final", StringComparison.OrdinalIgnoreCase))
            {
                return new AgentLoopResult(directive.Message ?? response.Content, turn, StoppedBecause.FinalAnswer);
            }

            var result = await ExecuteToolAsync(context, directive, cancellationToken).ConfigureAwait(false);

            await _conversationStore
                .AppendToolCallAsync(
                    conversationId,
                    new ToolCallRecord(
                        directive.ToolName ?? string.Empty,
                        directive.Arguments.ToJsonString(),
                        result.IsSuccess,
                        result.Content,
                        DateTimeOffset.UtcNow),
                    cancellationToken)
                .ConfigureAwait(false);

            await _conversationStore
                .AppendMessageAsync(
                    conversationId,
                    new AgentMessage("user", FormatToolObservation(directive, result), DateTimeOffset.UtcNow),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var message = $"Stopped after {options.MaxToolIterations} tool iterations.";
        await _conversationStore
            .AppendMessageAsync(
                conversationId,
                new AgentMessage("assistant", message, DateTimeOffset.UtcNow),
                cancellationToken)
            .ConfigureAwait(false);

        return new AgentLoopResult(message, options.MaxToolIterations, StoppedBecause.MaxIterations);
    }

    private async Task<ToolResult> ExecuteToolAsync(
        ToolExecutionContext context,
        AgentDirective directive,
        CancellationToken cancellationToken)
    {
        if (directive.ToolName is null || !_toolRegistry.TryGet(directive.ToolName, out var tool))
        {
            return ToolResult.Failure($"Unknown tool: {directive.ToolName}");
        }

        try
        {
            return await tool.ExecuteAsync(context, directive, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolResult.Failure($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string FormatToolObservation(AgentDirective directive, ToolResult result)
    {
        return $"""
            TOOL_RESULT
            tool: {directive.ToolName}
            success: {result.IsSuccess}
            arguments: {directive.Arguments.ToJsonString()}

            {result.Content}

            Respond with the next JSON object. Use type "tool" for another action or type "final" when finished.
            """;
    }
}
