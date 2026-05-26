using System.Net.Http.Headers;

namespace OpenContext.AgentBridge.Providers.OpenAiCompatible;

internal static class OpenAiCompatibleAuthentication
{
    public static void Apply(HttpRequestMessage request, OpenAiCompatibleOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return;
        }

        var headerName = string.IsNullOrWhiteSpace(options.ApiKeyHeader)
            ? "Authorization"
            : options.ApiKeyHeader.Trim();
        var hasPrefix = !string.IsNullOrWhiteSpace(options.ApiKeyPrefix);
        var value = hasPrefix
            ? $"{options.ApiKeyPrefix!.Trim()} {options.ApiKey}"
            : options.ApiKey;

        if (string.Equals(headerName, "Authorization", StringComparison.OrdinalIgnoreCase) && hasPrefix)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(options.ApiKeyPrefix!.Trim(), options.ApiKey);
            return;
        }

        request.Headers.TryAddWithoutValidation(headerName, value);
    }
}
