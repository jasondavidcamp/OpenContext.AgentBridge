using System.Text.Json;

namespace OpenContext.AgentBridge.Providers.Gemini;

internal static class GeminiTrafficLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static async Task WriteAsync(
        GeminiOptions options,
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
            endpoint = GeminiOptions.RedactEndpoint(endpoint),
            model = options.Model,
            request,
            response
        };
        var path = Path.Combine(
            options.LogDirectory,
            $"gemini-{startedAt:yyyyMMdd-HHmmss-fff}.json");

        await File.WriteAllTextAsync(
                path,
                JsonSerializer.Serialize(log, JsonOptions),
                cancellationToken)
            .ConfigureAwait(false);
    }

}
