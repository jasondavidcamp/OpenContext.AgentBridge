namespace OpenContext.AgentBridge.Core.Configuration;

public sealed class AgentBridgeConfig
{
    public string? ModelProvider { get; init; } = AgentBridgeDefaults.DefaultModelProvider;

    public string? DefaultExecutor { get; init; } = AgentBridgeDefaults.DefaultExecutor;

    public int? MaxIterations { get; init; } = AgentBridgeDefaults.DefaultMaxIterations;

    public bool? LogModelTraffic { get; init; } = false;

    public GeminiConfig Gemini { get; init; } = new();

    public OpenAiCompatibleConfig OpenAiCompatible { get; init; } = new();

    public static AgentBridgeConfig CreateDefault()
    {
        return new AgentBridgeConfig();
    }
}
