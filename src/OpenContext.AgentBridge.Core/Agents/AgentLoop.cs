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
            options.Progress?.Report(new AgentProgressEvent(
                AgentProgressKind.ModelRequest,
                turn,
                "Asking model for next action."));

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

            options.Progress?.Report(new AgentProgressEvent(
                AgentProgressKind.ModelResponse,
                turn,
                "Model response received.",
                Preview: Preview(response.Content)));

            await _conversationStore
                .AppendMessageAsync(
                    conversationId,
                    new AgentMessage("assistant", response.Content, DateTimeOffset.UtcNow),
                    cancellationToken)
                .ConfigureAwait(false);

            if (!AgentResponseParser.TryParse(response.Content, out var directive))
            {
                options.Progress?.Report(new AgentProgressEvent(
                    AgentProgressKind.InvalidModelResponse,
                    turn,
                    "Model response did not match the action protocol.",
                    Preview: Preview(response.Content)));

                await _conversationStore
                    .AppendMessageAsync(
                        conversationId,
                        new AgentMessage("user", FormatInvalidResponseObservation(response.Content), DateTimeOffset.UtcNow),
                        cancellationToken)
                    .ConfigureAwait(false);

                continue;
            }

            if (string.Equals(directive.Type, "final", StringComparison.OrdinalIgnoreCase))
            {
                var finalMessage = directive.Message ?? response.Content;
                options.Progress?.Report(new AgentProgressEvent(
                    AgentProgressKind.FinalAnswer,
                    turn,
                    "Final answer received.",
                    Preview: Preview(finalMessage)));

                return new AgentLoopResult(
                    finalMessage,
                    turn,
                    StoppedBecause.FinalAnswer,
                    await ReadToolCallsAsync(conversationId, cancellationToken).ConfigureAwait(false));
            }

            if (directive.ToolName is { } toolName
                && _toolRegistry.TryGetCanonicalName(toolName, out var canonicalToolName))
            {
                directive = directive with { ToolName = canonicalToolName };
            }

            options.Progress?.Report(new AgentProgressEvent(
                AgentProgressKind.ToolRequested,
                turn,
                $"Tool requested: {directive.ToolName}",
                directive.ToolName,
                directive.Arguments.ToJsonString()));

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

            options.Progress?.Report(new AgentProgressEvent(
                AgentProgressKind.ToolCompleted,
                turn,
                result.IsSuccess ? "Tool completed." : "Tool failed.",
                directive.ToolName,
                directive.Arguments.ToJsonString(),
                result.IsSuccess,
                Preview(result.Content)));

            await _conversationStore
                .AppendMessageAsync(
                    conversationId,
                    new AgentMessage(
                        "user",
                        FormatToolObservation(directive, result, options.MaxToolObservationCharacters),
                        DateTimeOffset.UtcNow),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var message = $"Stopped after {options.MaxToolIterations} tool iterations.";
        options.Progress?.Report(new AgentProgressEvent(
            AgentProgressKind.MaxIterations,
            options.MaxToolIterations,
            message));

        await _conversationStore
            .AppendMessageAsync(
                conversationId,
                new AgentMessage("assistant", message, DateTimeOffset.UtcNow),
                cancellationToken)
            .ConfigureAwait(false);

        return new AgentLoopResult(
            message,
            options.MaxToolIterations,
            StoppedBecause.MaxIterations,
            await ReadToolCallsAsync(conversationId, cancellationToken).ConfigureAwait(false));
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

    private static string FormatToolObservation(
        AgentDirective directive,
        ToolResult result,
        int maxContentCharacters)
    {
        var content = CompactToolObservationContent(result.Content, maxContentCharacters);

        return $"""
            TOOL_RESULT
            tool: {directive.ToolName}
            success: {result.IsSuccess}
            arguments: {directive.Arguments.ToJsonString()}

            {content}

            Respond with the next JSON object. Use type "tool" for another action or type "final" when finished.
            """;
    }

    private static string CompactToolObservationContent(string content, int maxCharacters)
    {
        if (maxCharacters <= 0 || content.Length <= maxCharacters)
        {
            return content;
        }

        const int minimumMaxCharacters = 200;
        var effectiveMaxCharacters = Math.Max(maxCharacters, minimumMaxCharacters);
        var marker = $"{Environment.NewLine}[tool result truncated from {content.Length} to {effectiveMaxCharacters} characters; middle omitted]{Environment.NewLine}";
        var contentBudget = effectiveMaxCharacters - marker.Length;
        if (contentBudget <= 0)
        {
            return content[..Math.Min(content.Length, effectiveMaxCharacters)];
        }

        var headCharacters = Math.Max(1, contentBudget / 2);
        var tailCharacters = Math.Max(1, contentBudget - headCharacters);

        return content[..headCharacters] + marker + content[^tailCharacters..];
    }

    private static string FormatInvalidResponseObservation(string response)
    {
        const string toolExample = """{"type":"tool","tool":"read_file","arguments":{"path":"README.md"}}""";
        const string finalExample = """{"type":"final","message":"Short summary of the result."}""";

        return $"""
            MODEL_RESPONSE_PARSE_ERROR
            The previous response could not be parsed as an AgentBridge action.

            Response preview:
            {Preview(response, 1_200)}

            Respond with exactly one JSON object and no surrounding prose.
            You are operating only through AgentBridge tools in the workspace already provided by the system prompt. Do not claim direct filesystem access or mention paths unless a tool result showed them.
            Use one of these forms:
            {toolExample}
            {finalExample}
            """;
    }

    private Task<IReadOnlyList<ToolCallRecord>> ReadToolCallsAsync(
        string conversationId,
        CancellationToken cancellationToken)
    {
        return _conversationStore.ReadToolCallsAsync(conversationId, cancellationToken);
    }

    private static string Preview(string value, int maxCharacters = 400)
    {
        var oneLine = value
            .ReplaceLineEndings(" ")
            .Trim();

        return oneLine.Length <= maxCharacters
            ? oneLine
            : oneLine[..maxCharacters] + "...";
    }
}
