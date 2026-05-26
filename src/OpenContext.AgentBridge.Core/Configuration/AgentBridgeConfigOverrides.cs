namespace OpenContext.AgentBridge.Core.Configuration;

public sealed record AgentBridgeConfigOverrides(
    string? DefaultExecutor = null,
    int? MaxIterations = null,
    string? GeminiEndpoint = null,
    string? GeminiModel = null,
    string? GeminiApiKey = null,
    bool? LogModelTraffic = null,
    string? ModelProvider = null,
    string? OpenAiCompatibleEndpoint = null,
    string? OpenAiCompatibleModel = null,
    string? OpenAiCompatibleApiKey = null,
    string? OpenAiCompatibleApiKeyHeader = null,
    string? OpenAiCompatibleApiKeyPrefix = null);
