using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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
    endpoints = new[] { "/v1/models", "/v1/chat/completions" }
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

    if (chatRequest.Stream is true)
    {
        await WriteErrorAsync(
            context,
            StatusCodes.Status400BadRequest,
            "agentbridge-agent does not support streaming yet. Use stream=false.",
            cancellationToken);
        return;
    }

    var response = await RunAgentAsync(chatRequest, httpClientFactory.CreateClient(), cancellationToken);
    context.Response.Headers["X-AgentBridge-Conversation"] = response.ConversationId;
    context.Response.Headers["X-AgentBridge-Tool-Calls"] = response.ToolCallCount.ToString();
    await context.Response.WriteAsJsonAsync(response.Completion, AgentBridgeServerJson.Options, cancellationToken);
});

app.Run();

static async Task<AgentCompletionResult> RunAgentAsync(
    ChatCompletionRequest request,
    HttpClient httpClient,
    CancellationToken cancellationToken)
{
    var workspace = ResolveWorkspace();
    workspace.EnsureLocalState();

    var config = await ReadConfigAsync(workspace, cancellationToken);
    var store = new SqliteConversationStore(workspace.ConversationDatabasePath);
    await store.InitializeAsync(cancellationToken);

    var conversationId = await store.CreateConversationAsync(workspace.RootPath, cancellationToken);
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

    return new AgentCompletionResult(
        ChatCompletionResponse.Create(AgentModelId, result.FinalMessage),
        conversationId,
        result.ToolCalls.Count);
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

    var options = CreateOpenAiCompatibleOptions(workspace, config, null);
    using var upstreamRequest = new HttpRequestMessage(HttpMethod.Post, options.GetChatCompletionsEndpoint())
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };
    ApplyOpenAiCompatibleAuthentication(upstreamRequest, options);

    using var upstreamResponse = await httpClient.SendAsync(
        upstreamRequest,
        HttpCompletionOption.ResponseHeadersRead,
        cancellationToken);

    var responseBody = await upstreamResponse.Content.ReadAsByteArrayAsync(cancellationToken);
    context.Response.StatusCode = (int)upstreamResponse.StatusCode;
    context.Response.ContentType = upstreamResponse.Content.Headers.ContentType?.ToString()
        ?? "application/json";
    await context.Response.Body.WriteAsync(responseBody, cancellationToken);
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
    [property: JsonPropertyName("usage")] Usage? Usage)
{
    public static ChatCompletionResponse Create(string model, string content)
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
            null);
    }
}

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
