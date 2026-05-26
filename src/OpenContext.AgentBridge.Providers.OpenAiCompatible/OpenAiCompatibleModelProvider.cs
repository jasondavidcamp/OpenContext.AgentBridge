using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenContext.AgentBridge.Core.Conversation;
using OpenContext.AgentBridge.Core.Models;

namespace OpenContext.AgentBridge.Providers.OpenAiCompatible;

public sealed class OpenAiCompatibleModelProvider : IModelProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly OpenAiCompatibleOptions _options;

    public OpenAiCompatibleModelProvider(HttpClient httpClient, OpenAiCompatibleOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public string Name => "openai-compatible";

    public async Task<AgentTurnResponse> CompleteAsync(
        AgentTurnRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Model))
        {
            throw new InvalidOperationException(
                "OpenAI-compatible provider is not configured. Set AGENTBRIDGE_OPENAI_MODEL or AGENTBRIDGE_STARK_MODEL, or configure .agentbridge/config.json.");
        }

        var endpoint = _options.GetChatCompletionsEndpoint();
        var payload = CreatePayload(request);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };
        OpenAiCompatibleAuthentication.Apply(httpRequest, _options);

        var startedAt = DateTimeOffset.UtcNow;
        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        await OpenAiCompatibleTrafficLogger.WriteAsync(
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
            throw new InvalidOperationException(BuildHttpErrorMessage(response, endpoint, body));
        }

        return new AgentTurnResponse(OpenAiCompatibleResponseTextExtractor.Extract(body));
    }

    private ChatCompletionRequest CreatePayload(AgentTurnRequest request)
    {
        var messages = new List<ChatMessage>
        {
            new("system", AgentSystemPromptBuilder.Build(request))
        };
        messages.AddRange(request.Messages.Select(ToOpenAiMessage));

        return new ChatCompletionRequest(
            _options.Model,
            messages,
            Stream: false,
            _options.Temperature,
            _options.MaxTokens);
    }

    private static ChatMessage ToOpenAiMessage(AgentMessage message)
    {
        var role = message.Role.ToLowerInvariant() switch
        {
            "assistant" => "assistant",
            "system" => "system",
            _ => "user"
        };

        return new ChatMessage(role, message.Content);
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
            502 => " Upstream LLM service returned an error.",
            _ => string.Empty
        };
        var rateLimit = DescribeRateLimitHeaders(response);

        return $"OpenAI-compatible request failed with {status} {response.ReasonPhrase} at {OpenAiCompatibleOptions.RedactEndpoint(endpoint)}.{hint}{rateLimit} Response: {Preview(body)}";
    }

    private static string DescribeRateLimitHeaders(HttpResponseMessage response)
    {
        var interestingHeaders = new[]
        {
            "X-RateLimit-Remaining-Tokens",
            "X-RateLimit-Limit-Tokens",
            "X-RateLimit-Remaining-Requests",
            "X-RateLimit-Limit-Requests"
        };
        var values = interestingHeaders
            .Select(header => response.Headers.TryGetValues(header, out var headerValues)
                ? $"{header}: {string.Join(",", headerValues)}"
                : null)
            .Where(value => value is not null)
            .ToArray();

        return values.Length == 0
            ? string.Empty
            : " Rate limit headers: " + string.Join("; ", values) + ".";
    }

    private static string Preview(string value, int maxCharacters = 1_500)
    {
        var preview = value.ReplaceLineEndings(" ").Trim();

        return preview.Length <= maxCharacters
            ? preview
            : preview[..maxCharacters] + "...";
    }

    private sealed record ChatCompletionRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("temperature")] float? Temperature,
        [property: JsonPropertyName("max_tokens")] int? MaxTokens);

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);
}
