using System.Text.RegularExpressions;
using OpenContext.AgentBridge.Core;

namespace OpenContext.AgentBridge.Providers.OpenAiCompatible;

public sealed class OpenAiCompatibleOptions
{
    public string Model { get; init; } = AgentBridgeDefaults.DefaultOpenAiCompatibleModel;

    public string? Endpoint { get; init; }

    public string? ApiKey { get; init; }

    public string ApiKeyHeader { get; init; } = AgentBridgeDefaults.DefaultOpenAiCompatibleApiKeyHeader;

    public string? ApiKeyPrefix { get; init; } = AgentBridgeDefaults.DefaultOpenAiCompatibleApiKeyPrefix;

    public float? Temperature { get; init; }

    public int? MaxTokens { get; init; }

    public bool LogModelTraffic { get; init; }

    public string? LogDirectory { get; init; }

    public Uri GetChatCompletionsEndpoint()
    {
        var endpoint = GetConfiguredEndpoint();
        var path = endpoint.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return endpoint;
        }

        if (IsKnownOpenAiCompatibleBasePath(path))
        {
            return WithPath(endpoint, path + "/chat/completions");
        }

        return WithPath(endpoint, CombinePath(path, "v1/chat/completions"));
    }

    public Uri GetModelsEndpoint()
    {
        var endpoint = GetConfiguredEndpoint();
        var path = endpoint.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
        {
            return endpoint;
        }

        if (path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            var basePath = path[..^"/chat/completions".Length];
            return WithPath(endpoint, basePath + "/models");
        }

        if (IsKnownOpenAiCompatibleBasePath(path))
        {
            return WithPath(endpoint, path + "/models");
        }

        return WithPath(endpoint, CombinePath(path, "v1/models"));
    }

    public string GetRedactedEndpoint()
    {
        return RedactEndpoint(GetChatCompletionsEndpoint());
    }

    public static string RedactEndpoint(Uri endpoint)
    {
        var redacted = Regex.Replace(
            endpoint.ToString(),
            "([?&](?:key|api_key|token|access_token)=)[^&]+",
            "$1<redacted>",
            RegexOptions.IgnoreCase);

        if (!string.IsNullOrWhiteSpace(endpoint.UserInfo))
        {
            redacted = redacted.Replace(endpoint.UserInfo + "@", "<redacted>@");
        }

        return redacted;
    }

    private Uri GetConfiguredEndpoint()
    {
        if (string.IsNullOrWhiteSpace(Endpoint))
        {
            throw new InvalidOperationException(
                "OpenAI-compatible provider is not configured. Set AGENTBRIDGE_OPENAI_ENDPOINT or AGENTBRIDGE_STARK_ENDPOINT, or configure .agentbridge/config.json.");
        }

        if (!Uri.TryCreate(Endpoint.Trim(), UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException($"OpenAI-compatible endpoint is not an absolute URI: {Endpoint}");
        }

        return endpoint;
    }

    private static Uri WithPath(Uri endpoint, string path)
    {
        var builder = new UriBuilder(endpoint)
        {
            Path = path
        };

        return builder.Uri;
    }

    private static bool IsKnownOpenAiCompatibleBasePath(string path)
    {
        return path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/openai", StringComparison.OrdinalIgnoreCase);
    }

    private static string CombinePath(string basePath, string suffix)
    {
        return string.IsNullOrWhiteSpace(basePath)
            ? "/" + suffix
            : basePath + "/" + suffix;
    }
}
