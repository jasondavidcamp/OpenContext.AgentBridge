namespace OpenContext.AgentBridge.Core.Configuration;

public sealed class GeminiConfig
{
    public string? Model { get; init; } = AgentBridgeDefaults.DefaultGeminiModel;

    public string? Endpoint { get; init; }

    public string? ApiKey { get; init; }
}
