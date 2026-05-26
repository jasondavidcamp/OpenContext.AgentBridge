namespace OpenContext.AgentBridge.Core.Configuration;

public sealed record EffectiveAgentBridgeConfig(
    string ModelProvider,
    string DefaultExecutor,
    int MaxIterations,
    bool LogModelTraffic,
    EffectiveGeminiConfig Gemini);

public sealed record EffectiveGeminiConfig(
    string Model,
    string? Endpoint,
    string? ApiKey);
