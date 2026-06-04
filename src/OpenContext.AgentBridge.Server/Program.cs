using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using OpenContext.AgentBridge.Core;
using OpenContext.AgentBridge.Core.Agents;
using OpenContext.AgentBridge.Core.Configuration;
using OpenContext.AgentBridge.Core.Conversation;
using OpenContext.AgentBridge.Core.Execution;
using OpenContext.AgentBridge.Core.Models;
using OpenContext.AgentBridge.Core.Skills;
using OpenContext.AgentBridge.Core.Tools;
using OpenContext.AgentBridge.Core.Tools.BuiltIn;
using OpenContext.AgentBridge.Core.Workspaces;
using OpenContext.AgentBridge.Providers.Gemini;
using OpenContext.AgentBridge.Providers.OpenAiCompatible;
using OpenContext.AgentBridge.Storage;

const string AgentModelId = "agentbridge-agent";

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddHttpClient();

var app = builder.Build();

app.MapGet("/", () => Results.Json(new
{
    name = "OpenContext.AgentBridge.Server",
    description = "Local OpenAI-compatible bridge for AgentBridge clients.",
    models = new[] { AgentModelId },
    endpoints = new[] { "/v1/models", "/v1/chat/completions", "/v1/agentbridge/conversations/{conversation_id}" }
}, AgentBridgeServerJson.Options));

app.MapGet("/v1/models", async (HttpContext context, CancellationToken cancellationToken) =>
{
    if (!AuthorizeLocalRequest(context))
    {
        return Results.Unauthorized();
    }

    var workspace = ResolveWorkspace();
    var config = await ReadConfigAsync(workspace, cancellationToken);
    var models = new List<ModelObject>
    {
        ModelObject.Create(AgentModelId, "agentbridge")
    };

    if (NormalizeProviderName(config.ModelProvider) == "openai-compatible"
        && !string.IsNullOrWhiteSpace(config.OpenAiCompatible.Model))
    {
        models.Add(ModelObject.Create(config.OpenAiCompatible.Model, "upstream"));
    }

    return Results.Json(new ModelsListResponse("list", models), AgentBridgeServerJson.Options);
});

app.MapPost("/v1/chat/completions", async Task (HttpContext context, IHttpClientFactory httpClientFactory, CancellationToken cancellationToken) =>
{
    if (!AuthorizeLocalRequest(context))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    var body = await new StreamReader(context.Request.Body).ReadToEndAsync(cancellationToken);
    if (!TryReadModel(body, out var model, out var error))
    {
        await WriteErrorAsync(context, StatusCodes.Status400BadRequest, error, cancellationToken);
        return;
    }

    if (!string.Equals(model, AgentModelId, StringComparison.OrdinalIgnoreCase))
    {
        await ProxyRawChatCompletionAsync(context, httpClientFactory.CreateClient(), body, cancellationToken);
        return;
    }

    if (!TryDeserialize<ChatCompletionRequest>(body, out var chatRequest, out error))
    {
        await WriteErrorAsync(context, StatusCodes.Status400BadRequest, error, cancellationToken);
        return;
    }

    AgentCompletionResult response;
    try
    {
        response = await RunAgentAsync(
            chatRequest,
            httpClientFactory.CreateClient(),
            ReadOptionalHeader(context, "X-AgentBridge-Conversation"),
            cancellationToken);
    }
    catch (AgentBridgeRequestException ex)
    {
        await WriteErrorAsync(context, ex.StatusCode, ex.Message, cancellationToken);
        return;
    }

    context.Response.Headers["X-AgentBridge-Conversation"] = response.ConversationId;
    context.Response.Headers["X-AgentBridge-Tool-Calls"] = response.ToolCallCount.ToString();
    if (chatRequest.Stream is true)
    {
        await WriteAgentStreamAsync(context, response.Completion, cancellationToken);
    }
    else
    {
        await context.Response.WriteAsJsonAsync(response.Completion, AgentBridgeServerJson.Options, cancellationToken);
    }
});

app.MapGet("/v1/agentbridge/conversations/{conversationId}", async Task (HttpContext context, string conversationId, CancellationToken cancellationToken) =>
{
    if (!AuthorizeLocalRequest(context))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    var workspace = ResolveWorkspace();
    var store = new SqliteConversationStore(workspace.ConversationDatabasePath);
    await store.InitializeAsync(cancellationToken);

    var conversation = (await store.ListConversationsAsync(workspace.RootPath, cancellationToken))
        .FirstOrDefault(candidate => string.Equals(candidate.Id, conversationId, StringComparison.OrdinalIgnoreCase));
    if (conversation is null)
    {
        await WriteErrorAsync(
            context,
            StatusCodes.Status404NotFound,
            $"Conversation not found: {conversationId}",
            cancellationToken);
        return;
    }

    var toolCalls = await store.ReadToolCallsAsync(conversation.Id, cancellationToken);
    await context.Response.WriteAsJsonAsync(
        CreateAgentBridgeConversationDetails(conversation, toolCalls),
        AgentBridgeServerJson.Options,
        cancellationToken);
});

app.Run();

static async Task<AgentCompletionResult> RunAgentAsync(
    ChatCompletionRequest request,
    HttpClient httpClient,
    string? requestedConversationId,
    CancellationToken cancellationToken)
{
    var workspace = ResolveWorkspace();
    workspace.EnsureLocalState();

    var config = await ReadConfigAsync(workspace, cancellationToken);
    var store = new SqliteConversationStore(workspace.ConversationDatabasePath);
    await store.InitializeAsync(cancellationToken);

    var conversationId = await ResolveConversationIdAsync(
        store,
        workspace,
        requestedConversationId,
        cancellationToken);
    foreach (var message in request.Messages)
    {
        await store.AppendMessageAsync(
            conversationId,
            new AgentMessage(NormalizeChatRole(message.Role), ToMessageContent(message.Content), DateTimeOffset.UtcNow),
            cancellationToken);
    }

    var modelProvider = CreateModelProvider(httpClient, workspace, config, request);
    var loop = new AgentLoop(
        modelProvider,
        store,
        new ToolRegistry(BuiltInTools.CreateDefault()));
    var result = await loop.RunAsync(
        conversationId,
        workspace,
        CreateExecutor(config),
        await LoadSkillsAsync(workspace, cancellationToken),
        new AgentLoopOptions(MaxToolIterations: config.MaxIterations),
        cancellationToken);
    var metadata = CreateAgentBridgeMetadata(conversationId, result.ToolCalls);

    return new AgentCompletionResult(
        ChatCompletionResponse.Create(AgentModelId, result.FinalMessage, metadata),
        conversationId,
        result.ToolCalls.Count);
}

static async Task<string> ResolveConversationIdAsync(
    IConversationStore store,
    WorkspaceContext workspace,
    string? requestedConversationId,
    CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(requestedConversationId))
    {
        return await store.CreateConversationAsync(workspace.RootPath, cancellationToken);
    }

    var normalizedConversationId = requestedConversationId.Trim();
    var conversation = (await store.ListConversationsAsync(workspace.RootPath, cancellationToken))
        .FirstOrDefault(candidate => string.Equals(
            candidate.Id,
            normalizedConversationId,
            StringComparison.OrdinalIgnoreCase));

    if (conversation is null)
    {
        throw new AgentBridgeRequestException(
            StatusCodes.Status404NotFound,
            $"Conversation not found: {normalizedConversationId}");
    }

    return conversation.Id;
}

static async Task ProxyRawChatCompletionAsync(
    HttpContext context,
    HttpClient httpClient,
    string body,
    CancellationToken cancellationToken)
{
    var workspace = ResolveWorkspace();
    var config = await ReadConfigAsync(workspace, cancellationToken);
    if (NormalizeProviderName(config.ModelProvider) != "openai-compatible")
    {
        await WriteErrorAsync(
            context,
            StatusCodes.Status400BadRequest,
            "Raw proxy mode requires AGENTBRIDGE_MODEL_PROVIDER=openai-compatible, gateway, or gemini-openai.",
            cancellationToken);
        return;
    }

    var forwardedBody = NormalizeRawChatCompletionBody(body);
    var options = CreateOpenAiCompatibleOptions(workspace, config, null);
    using var upstreamRequest = new HttpRequestMessage(HttpMethod.Post, options.GetChatCompletionsEndpoint())
    {
        Content = new StringContent(forwardedBody, Encoding.UTF8, "application/json")
    };
    ApplyOpenAiCompatibleAuthentication(upstreamRequest, options);

    var streamRequested = TryReadStream(forwardedBody);
    using var upstreamResponse = await httpClient.SendAsync(
        upstreamRequest,
        HttpCompletionOption.ResponseHeadersRead,
        cancellationToken);

    context.Response.StatusCode = (int)upstreamResponse.StatusCode;
    context.Response.ContentType = upstreamResponse.Content.Headers.ContentType?.ToString()
        ?? (streamRequested ? "text/event-stream" : "application/json");
    if (streamRequested)
    {
        await using var responseStream = await upstreamResponse.Content.ReadAsStreamAsync(cancellationToken);
        await responseStream.CopyToAsync(context.Response.Body, cancellationToken);
        return;
    }

    var responseBody = await upstreamResponse.Content.ReadAsByteArrayAsync(cancellationToken);
    await context.Response.Body.WriteAsync(responseBody, cancellationToken);
}

static string NormalizeRawChatCompletionBody(string body)
{
    var fieldsToStrip = new[]
    {
        "thinking",
        "effort",
        "reasoningSummary",
        "reasoning_effort",
        "stream_options"
    };

    JsonNode? node;
    try
    {
        node = JsonNode.Parse(body);
    }
    catch (JsonException)
    {
        return body;
    }

    if (node is not JsonObject request)
    {
        return body;
    }

    foreach (var propertyName in fieldsToStrip)
    {
        request.Remove(propertyName);
    }

    return request.ToJsonString(AgentBridgeServerJson.Options);
}

static async Task WriteAgentStreamAsync(
    HttpContext context,
    ChatCompletionResponse completion,
    CancellationToken cancellationToken)
{
    context.Response.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";
    context.Response.Headers["X-Accel-Buffering"] = "no";
    await context.Response.StartAsync(cancellationToken);

    await WriteSseDataAsync(
        context,
        ChatCompletionChunk.Create(
            completion.Id,
            completion.Created,
            completion.Model,
            new ChatDelta(Role: "assistant", Content: null),
            finishReason: null),
        cancellationToken);

    var content = completion.Choices.FirstOrDefault()?.Message.Content ?? string.Empty;
    foreach (var chunk in SplitStreamContent(content))
    {
        await WriteSseDataAsync(
            context,
            ChatCompletionChunk.Create(
                completion.Id,
                completion.Created,
                completion.Model,
                new ChatDelta(Role: null, Content: chunk),
                finishReason: null),
            cancellationToken);
    }

    await WriteSseDataAsync(
        context,
        ChatCompletionChunk.Create(
            completion.Id,
            completion.Created,
            completion.Model,
            new ChatDelta(Role: null, Content: null),
            finishReason: "stop",
            completion.AgentBridge),
        cancellationToken);
    await context.Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
    await context.Response.Body.FlushAsync(cancellationToken);
}

static AgentBridgeResponseMetadata CreateAgentBridgeMetadata(
    string conversationId,
    IReadOnlyList<ToolCallRecord> toolCalls)
{
    var successfulToolCalls = toolCalls.Count(toolCall => toolCall.IsSuccess);
    return new AgentBridgeResponseMetadata(
        conversationId,
        toolCalls.Count,
        successfulToolCalls,
        toolCalls.Count - successfulToolCalls);
}

static string PreviewToolResult(string value)
{
    const int maxCharacters = 400;
    var oneLine = value
        .ReplaceLineEndings(" ")
        .Trim();

    return oneLine.Length <= maxCharacters
        ? oneLine
        : oneLine[..maxCharacters] + "...";
}

static AgentBridgeConversationDetails CreateAgentBridgeConversationDetails(
    ConversationSummary conversation,
    IReadOnlyList<ToolCallRecord> toolCalls)
{
    return new AgentBridgeConversationDetails(
        conversation.Id,
        conversation.CreatedAt,
        conversation.UpdatedAt,
        CreateAgentBridgeMetadata(conversation.Id, toolCalls),
        toolCalls.Select(toolCall => new AgentBridgeToolCallDetails(
                toolCall.ToolName,
                toolCall.ArgumentsJson,
                toolCall.IsSuccess,
                PreviewToolResult(toolCall.ResultContent),
                toolCall.CreatedAt))
            .ToArray());
}

static async Task WriteSseDataAsync(
    HttpContext context,
    object value,
    CancellationToken cancellationToken)
{
    var json = JsonSerializer.Serialize(value, AgentBridgeServerJson.Options);
    await context.Response.WriteAsync($"data: {json}\n\n", cancellationToken);
    await context.Response.Body.FlushAsync(cancellationToken);
}

static IEnumerable<string> SplitStreamContent(string content)
{
    const int chunkSize = 256;
    for (var index = 0; index < content.Length; index += chunkSize)
    {
        yield return content.Substring(index, Math.Min(chunkSize, content.Length - index));
    }
}

static bool AuthorizeLocalRequest(HttpContext context)
{
    var expectedApiKey = Environment.GetEnvironmentVariable("AGENTBRIDGE_SERVER_API_KEY");
    if (string.IsNullOrWhiteSpace(expectedApiKey))
    {
        return true;
    }

    if (!context.Request.Headers.TryGetValue("Authorization", out var authorization))
    {
        return false;
    }

    var expected = $"Bearer {expectedApiKey.Trim()}";
    return authorization.Any(value => string.Equals(value, expected, StringComparison.Ordinal));
}

static string? ReadOptionalHeader(HttpContext context, string headerName)
{
    if (!context.Request.Headers.TryGetValue(headerName, out var values))
    {
        return null;
    }

    var value = values.FirstOrDefault();
    return string.IsNullOrWhiteSpace(value)
        ? null
        : value.Trim();
}

static WorkspaceContext ResolveWorkspace()
{
    var workspacePath = Environment.GetEnvironmentVariable("AGENTBRIDGE_SERVER_WORKSPACE");
    if (string.IsNullOrWhiteSpace(workspacePath))
    {
        workspacePath = Directory.GetCurrentDirectory();
    }

    return WorkspaceContext.FromPath(workspacePath);
}

static async Task<EffectiveAgentBridgeConfig> ReadConfigAsync(
    WorkspaceContext workspace,
    CancellationToken cancellationToken)
{
    return await new AgentBridgeConfigStore()
        .ReadEffectiveAsync(workspace, cancellationToken: cancellationToken)
        .ConfigureAwait(false);
}

static async Task<IReadOnlyList<Skill>> LoadSkillsAsync(
    WorkspaceContext workspace,
    CancellationToken cancellationToken)
{
    var loader = new SkillLoader();
    return await loader.LoadAsync(new[]
    {
        workspace.SkillsPath,
        Path.Combine(workspace.RootPath, AgentBridgeDefaults.SkillsDirectoryName)
    }, cancellationToken);
}

static IModelProvider CreateModelProvider(
    HttpClient httpClient,
    WorkspaceContext workspace,
    EffectiveAgentBridgeConfig config,
    ChatCompletionRequest request)
{
    return NormalizeProviderName(config.ModelProvider) switch
    {
        "gemini" => new GeminiModelProvider(httpClient, CreateGeminiOptions(workspace, config)),
        "openai-compatible" => new OpenAiCompatibleModelProvider(
            httpClient,
            CreateOpenAiCompatibleOptions(workspace, config, request)),
        _ => throw new ArgumentException($"Unknown model provider: {config.ModelProvider}")
    };
}

static GeminiOptions CreateGeminiOptions(
    WorkspaceContext workspace,
    EffectiveAgentBridgeConfig config)
{
    return new GeminiOptions
    {
        ApiKey = config.Gemini.ApiKey,
        Endpoint = config.Gemini.Endpoint,
        Model = config.Gemini.Model,
        LogModelTraffic = config.LogModelTraffic,
        LogDirectory = workspace.LogsPath
    };
}

static OpenAiCompatibleOptions CreateOpenAiCompatibleOptions(
    WorkspaceContext workspace,
    EffectiveAgentBridgeConfig config,
    ChatCompletionRequest? request)
{
    return new OpenAiCompatibleOptions
    {
        ApiKey = config.OpenAiCompatible.ApiKey,
        ApiKeyHeader = config.OpenAiCompatible.ApiKeyHeader,
        ApiKeyPrefix = config.OpenAiCompatible.ApiKeyPrefix,
        Endpoint = config.OpenAiCompatible.Endpoint,
        Model = config.OpenAiCompatible.Model,
        Temperature = request?.Temperature ?? config.OpenAiCompatible.Temperature,
        MaxTokens = request?.MaxTokens ?? config.OpenAiCompatible.MaxTokens,
        LogModelTraffic = config.LogModelTraffic,
        LogDirectory = workspace.LogsPath
    };
}

static IWorkspaceExecutor CreateExecutor(EffectiveAgentBridgeConfig config)
{
    return config.DefaultExecutor.Equals("docker", StringComparison.OrdinalIgnoreCase)
        ? new DockerWorkspaceExecutor(Environment.GetEnvironmentVariable("AGENTBRIDGE_DOCKER_IMAGE"))
        : new HostWorkspaceExecutor();
}

static string NormalizeProviderName(string provider)
{
    return provider.ToLowerInvariant() switch
    {
        "openai" or "openai-compatible" or "gateway" or "gemini-openai" or "gemini-openai-compatible" => "openai-compatible",
        var normalized => normalized
    };
}

static string NormalizeChatRole(string role)
{
    return role.ToLowerInvariant() switch
    {
        "assistant" => "assistant",
        "system" => "system",
        _ => "user"
    };
}

static string ToMessageContent(JsonElement content)
{
    return content.ValueKind == JsonValueKind.String
        ? content.GetString() ?? string.Empty
        : content.GetRawText();
}

static bool TryReadModel(string body, out string model, out string error)
{
    model = string.Empty;
    error = string.Empty;

    try
    {
        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("model", out var modelElement)
            || modelElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(modelElement.GetString()))
        {
            error = "Chat completion request must include a model.";
            return false;
        }

        model = modelElement.GetString()!;
        return true;
    }
    catch (JsonException ex)
    {
        error = $"Invalid JSON request body: {ex.Message}";
        return false;
    }
}

static bool TryReadStream(string body)
{
    try
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty("stream", out var streamElement)
               && streamElement.ValueKind == JsonValueKind.True;
    }
    catch (JsonException)
    {
        return false;
    }
}

static bool TryDeserialize<T>(string body, out T value, out string error)
{
    value = default!;
    error = string.Empty;

    try
    {
        value = JsonSerializer.Deserialize<T>(body, AgentBridgeServerJson.Options)!;
        if (value is null)
        {
            error = "Request body was empty.";
            return false;
        }

        return true;
    }
    catch (JsonException ex)
    {
        error = $"Invalid JSON request body: {ex.Message}";
        return false;
    }
}

static async Task WriteErrorAsync(
    HttpContext context,
    int statusCode,
    string message,
    CancellationToken cancellationToken)
{
    context.Response.StatusCode = statusCode;
    await context.Response.WriteAsJsonAsync(
        new ErrorResponse(new ErrorObject(message, "invalid_request_error")),
        AgentBridgeServerJson.Options,
        cancellationToken);
}

static void ApplyOpenAiCompatibleAuthentication(
    HttpRequestMessage request,
    OpenAiCompatibleOptions options)
{
    if (string.IsNullOrWhiteSpace(options.ApiKey))
    {
        return;
    }

    var headerName = string.IsNullOrWhiteSpace(options.ApiKeyHeader)
        ? "Authorization"
        : options.ApiKeyHeader.Trim();
    var hasPrefix = !string.IsNullOrWhiteSpace(options.ApiKeyPrefix);
    var value = hasPrefix
        ? $"{options.ApiKeyPrefix!.Trim()} {options.ApiKey}"
        : options.ApiKey;

    if (string.Equals(headerName, "Authorization", StringComparison.OrdinalIgnoreCase) && hasPrefix)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue(options.ApiKeyPrefix!.Trim(), options.ApiKey);
        return;
    }

    request.Headers.TryAddWithoutValidation(headerName, value);
}

internal static class AgentBridgeServerJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };
}

internal sealed record AgentCompletionResult(
    ChatCompletionResponse Completion,
    string ConversationId,
    int ToolCallCount);

internal sealed class AgentBridgeRequestException : Exception
{
    public AgentBridgeRequestException(int statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}

internal sealed record ChatCompletionRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
    [property: JsonPropertyName("stream")] bool? Stream = null,
    [property: JsonPropertyName("temperature")] float? Temperature = null,
    [property: JsonPropertyName("max_tokens")] int? MaxTokens = null);

internal sealed record ChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] JsonElement Content);

internal sealed record ChatCompletionResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("object")] string Object,
    [property: JsonPropertyName("created")] long Created,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("choices")] IReadOnlyList<ChatChoice> Choices,
    [property: JsonPropertyName("usage")] Usage? Usage,
    [property: JsonPropertyName("agentbridge")] AgentBridgeResponseMetadata? AgentBridge)
{
    public static ChatCompletionResponse Create(
        string model,
        string content,
        AgentBridgeResponseMetadata? agentBridge = null)
    {
        return new ChatCompletionResponse(
            $"chatcmpl-agentbridge-{Guid.NewGuid():N}",
            "chat.completion",
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            model,
            new[]
            {
                new ChatChoice(
                    0,
                    new ChatResponseMessage("assistant", content),
                    "stop")
            },
            null,
            agentBridge);
    }
}

internal sealed record ChatCompletionChunk(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("object")] string Object,
    [property: JsonPropertyName("created")] long Created,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("choices")] IReadOnlyList<ChatChunkChoice> Choices,
    [property: JsonPropertyName("agentbridge")] AgentBridgeResponseMetadata? AgentBridge = null)
{
    public static ChatCompletionChunk Create(
        string id,
        long created,
        string model,
        ChatDelta delta,
        string? finishReason,
        AgentBridgeResponseMetadata? agentBridge = null)
    {
        return new ChatCompletionChunk(
            id,
            "chat.completion.chunk",
            created,
            model,
            new[]
            {
                new ChatChunkChoice(0, delta, finishReason)
            },
            agentBridge);
    }
}

internal sealed record ChatChunkChoice(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("delta")] ChatDelta Delta,
    [property: JsonPropertyName("finish_reason")] string? FinishReason);

internal sealed record ChatDelta(
    [property: JsonPropertyName("role")] string? Role,
    [property: JsonPropertyName("content")] string? Content);

internal sealed record ChatChoice(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("message")] ChatResponseMessage Message,
    [property: JsonPropertyName("finish_reason")] string FinishReason);

internal sealed record ChatResponseMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

internal sealed record Usage(
    [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
    [property: JsonPropertyName("completion_tokens")] int CompletionTokens,
    [property: JsonPropertyName("total_tokens")] int TotalTokens);

internal sealed record AgentBridgeResponseMetadata(
    [property: JsonPropertyName("conversation_id")] string ConversationId,
    [property: JsonPropertyName("tool_call_count")] int ToolCallCount,
    [property: JsonPropertyName("successful_tool_call_count")] int SuccessfulToolCallCount,
    [property: JsonPropertyName("failed_tool_call_count")] int FailedToolCallCount);

internal sealed record AgentBridgeConversationDetails(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("agentbridge")] AgentBridgeResponseMetadata AgentBridge,
    [property: JsonPropertyName("tool_calls")] IReadOnlyList<AgentBridgeToolCallDetails> ToolCalls);

internal sealed record AgentBridgeToolCallDetails(
    [property: JsonPropertyName("tool_name")] string ToolName,
    [property: JsonPropertyName("arguments_json")] string ArgumentsJson,
    [property: JsonPropertyName("is_success")] bool IsSuccess,
    [property: JsonPropertyName("result_preview")] string ResultPreview,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

internal sealed record ModelsListResponse(
    [property: JsonPropertyName("object")] string Object,
    [property: JsonPropertyName("data")] IReadOnlyList<ModelObject> Data);

internal sealed record ModelObject(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("object")] string Object,
    [property: JsonPropertyName("created")] long Created,
    [property: JsonPropertyName("owned_by")] string OwnedBy)
{
    public static ModelObject Create(string id, string ownedBy)
    {
        return new ModelObject(id, "model", DateTimeOffset.UtcNow.ToUnixTimeSeconds(), ownedBy);
    }
}

internal sealed record ErrorResponse([property: JsonPropertyName("error")] ErrorObject Error);

internal sealed record ErrorObject(
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("type")] string Type);
