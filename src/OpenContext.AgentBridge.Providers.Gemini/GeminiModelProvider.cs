using System.Net.Http.Json;
using System.Text.Json;
using OpenContext.AgentBridge.Core.Conversation;
using OpenContext.AgentBridge.Core.Models;

namespace OpenContext.AgentBridge.Providers.Gemini;

public sealed class GeminiModelProvider : IModelProvider
{
    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _options;

    public GeminiModelProvider(HttpClient httpClient, GeminiOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public string Name => "gemini";

    public async Task<AgentTurnResponse> CompleteAsync(
        AgentTurnRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) && string.IsNullOrWhiteSpace(_options.Endpoint))
        {
            throw new InvalidOperationException(
                "Gemini is not configured. Set AGENTBRIDGE_GEMINI_API_KEY, AGENTBRIDGE_GEMINI_ENDPOINT, or configure .agentbridge/config.json.");
        }

        var endpoint = _options.GetEndpoint();
        var payload = new
        {
            system_instruction = new
            {
                parts = new[]
                {
                    new { text = AgentSystemPromptBuilder.Build(request) }
                }
            },
            contents = request.Messages.Select(ToGeminiContent).ToArray()
        };

        var startedAt = DateTimeOffset.UtcNow;
        using var response = await _httpClient
            .PostAsJsonAsync(endpoint, payload, cancellationToken)
            .ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        await GeminiTrafficLogger.WriteAsync(
                _options,
                endpoint,
                startedAt,
                (int)response.StatusCode,
                payload,
                body,
                cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                BuildHttpErrorMessage(response, endpoint, body));
        }

        return new AgentTurnResponse(GeminiResponseTextExtractor.Extract(body));
    }

    private static object ToGeminiContent(AgentMessage message)
    {
        var role = string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)
            ? "model"
            : "user";

        return new
        {
            role,
            parts = new[]
            {
                new { text = message.Content }
            }
        };
    }

    private static string BuildHttpErrorMessage(HttpResponseMessage response, Uri endpoint, string body)
    {
        var status = (int)response.StatusCode;
        var hint = status switch
        {
            401 => " Check API key or endpoint authentication.",
            403 => " Check endpoint access and model permissions.",
            404 => " Check endpoint URL and model name.",
            429 => " Rate limit or quota exceeded.",
            _ => string.Empty
        };

        return $"Gemini request failed with {status} {response.ReasonPhrase} at {GeminiOptions.RedactEndpoint(endpoint)}.{hint} Response: {Preview(body)}";
    }

    private static string Preview(string value, int maxCharacters = 1_500)
    {
        var preview = value.ReplaceLineEndings(" ").Trim();

        return preview.Length <= maxCharacters
            ? preview
            : preview[..maxCharacters] + "...";
    }
}
