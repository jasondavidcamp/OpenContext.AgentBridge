using System.Text.Json;

namespace OpenContext.AgentBridge.Providers.OpenAiCompatible;

public sealed class OpenAiCompatibleModelCatalogClient
{
    private readonly HttpClient _httpClient;
    private readonly OpenAiCompatibleOptions _options;

    public OpenAiCompatibleModelCatalogClient(HttpClient httpClient, OpenAiCompatibleOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<IReadOnlyList<OpenAiCompatibleModel>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var endpoint = _options.GetModelsEndpoint();
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        OpenAiCompatibleAuthentication.Apply(request, _options);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(BuildHttpErrorMessage(response, endpoint, body));
        }

        return ParseModels(body);
    }

    public static IReadOnlyList<OpenAiCompatibleModel> ParseModels(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("OpenAI-compatible models response did not contain a data array.");
        }

        var models = new List<OpenAiCompatibleModel>();
        foreach (var item in data.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var idElement)
                ? idElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var ownedBy = item.TryGetProperty("owned_by", out var ownedByElement)
                ? ownedByElement.GetString()
                : null;
            var created = item.TryGetProperty("created", out var createdElement)
                && createdElement.TryGetInt64(out var createdValue)
                    ? createdValue
                    : 0;

            models.Add(new OpenAiCompatibleModel(id, ownedBy, created));
        }

        return models;
    }

    private static string BuildHttpErrorMessage(HttpResponseMessage response, Uri endpoint, string body)
    {
        var status = (int)response.StatusCode;
        var hint = status switch
        {
            401 => " Check API key or endpoint authentication.",
            403 => " Check endpoint access and model permissions.",
            404 => " Check endpoint URL.",
            429 => " Rate limit or quota exceeded.",
            502 => " Upstream LLM service returned an error.",
            _ => string.Empty
        };

        return $"OpenAI-compatible models request failed with {status} {response.ReasonPhrase} at {OpenAiCompatibleOptions.RedactEndpoint(endpoint)}.{hint} Response: {Preview(body)}";
    }

    private static string Preview(string value, int maxCharacters = 1_500)
    {
        var preview = value.ReplaceLineEndings(" ").Trim();

        return preview.Length <= maxCharacters
            ? preview
            : preview[..maxCharacters] + "...";
    }
}

public sealed record OpenAiCompatibleModel(
    string Id,
    string? OwnedBy,
    long Created);
