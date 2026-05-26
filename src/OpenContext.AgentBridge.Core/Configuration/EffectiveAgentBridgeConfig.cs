namespace OpenContext.AgentBridge.Core.Configuration;

public sealed record EffectiveAgentBridgeConfig(
    string ModelProvider,
    string DefaultExecutor,
    int MaxIterations,
    bool LogModelTraffic,
    EffectiveGeminiConfig Gemini,
    EffectiveOpenAiCompatibleConfig OpenAiCompatible);

public sealed record EffectiveGeminiConfig(
    string Model,
    string? Endpoint,
    string? ApiKey);

public sealed record EffectiveOpenAiCompatibleConfig(
    string Model,
    string? Endpoint,
    string? ApiKey,
    string ApiKeyHeader,
    string? ApiKeyPrefix,
    float? Temperature,
    int? MaxTokens);
