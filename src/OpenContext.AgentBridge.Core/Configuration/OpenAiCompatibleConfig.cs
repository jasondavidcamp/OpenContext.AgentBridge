namespace OpenContext.AgentBridge.Core.Configuration;

public sealed class OpenAiCompatibleConfig
{
    public string? Model { get; init; } = AgentBridgeDefaults.DefaultOpenAiCompatibleModel;

    public string? Endpoint { get; init; }

    public string? ApiKey { get; init; }

    public string? ApiKeyHeader { get; init; } = AgentBridgeDefaults.DefaultOpenAiCompatibleApiKeyHeader;

    public string? ApiKeyPrefix { get; init; } = AgentBridgeDefaults.DefaultOpenAiCompatibleApiKeyPrefix;

    public float? Temperature { get; init; }

    public int? MaxTokens { get; init; }

    public int? RequestTimeoutSeconds { get; init; }
}
