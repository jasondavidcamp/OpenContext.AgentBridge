namespace OpenContext.AgentBridge.Providers.Gemini;

public sealed class GeminiOptions
{
    public string Model { get; init; } = "gemini-1.5-pro";

    public string? ApiKey { get; init; }

    public string? Endpoint { get; init; }

    public Uri GetEndpoint()
    {
        if (!string.IsNullOrWhiteSpace(Endpoint))
        {
            return new Uri(Endpoint);
        }

        return new Uri(
            $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(Model)}:generateContent?key={Uri.EscapeDataString(ApiKey ?? string.Empty)}");
    }
}
