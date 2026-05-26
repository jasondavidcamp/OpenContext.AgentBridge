using System.Text.Json;

namespace OpenContext.AgentBridge.Providers.OpenAiCompatible;

internal static class OpenAiCompatibleTrafficLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static async Task WriteAsync(
        OpenAiCompatibleOptions options,
        Uri endpoint,
        DateTimeOffset startedAt,
        int statusCode,
        object request,
        string response,
        CancellationToken cancellationToken = default)
    {
        if (!options.LogModelTraffic || string.IsNullOrWhiteSpace(options.LogDirectory))
        {
            return;
        }

        Directory.CreateDirectory(options.LogDirectory);

        var log = new
        {
            startedAt,
            statusCode,
            endpoint = OpenAiCompatibleOptions.RedactEndpoint(endpoint),
            model = options.Model,
            request,
            response
        };
        var path = Path.Combine(
            options.LogDirectory,
            $"openai-compatible-{startedAt:yyyyMMdd-HHmmss-fff}.json");

        await File.WriteAllTextAsync(
                path,
                JsonSerializer.Serialize(log, JsonOptions),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
