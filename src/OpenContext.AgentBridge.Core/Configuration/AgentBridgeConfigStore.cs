using System.Text.Json;
using OpenContext.AgentBridge.Core.Workspaces;

namespace OpenContext.AgentBridge.Core.Configuration;

public sealed class AgentBridgeConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public async Task<AgentBridgeConfig> ReadAsync(
        WorkspaceContext workspace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        if (!File.Exists(workspace.ConfigPath))
        {
            return AgentBridgeConfig.CreateDefault();
        }

        await using var stream = File.OpenRead(workspace.ConfigPath);
        return await JsonSerializer
            .DeserializeAsync<AgentBridgeConfig>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? AgentBridgeConfig.CreateDefault();
    }

    public async Task<bool> WriteDefaultAsync(
        WorkspaceContext workspace,
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        workspace.EnsureLocalState();

        if (File.Exists(workspace.ConfigPath) && !overwrite)
        {
            return false;
        }

        await WriteAsync(workspace, AgentBridgeConfig.CreateDefault(), cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    public async Task WriteAsync(
        WorkspaceContext workspace,
        AgentBridgeConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(config);
        workspace.EnsureLocalState();

        await using var stream = File.Create(workspace.ConfigPath);
        await JsonSerializer
            .SerializeAsync(stream, config, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<EffectiveAgentBridgeConfig> ReadEffectiveAsync(
        WorkspaceContext workspace,
        AgentBridgeConfigOverrides? overrides = null,
        CancellationToken cancellationToken = default)
    {
        var config = await ReadAsync(workspace, cancellationToken).ConfigureAwait(false);
        overrides ??= new AgentBridgeConfigOverrides();
        var gemini = config.Gemini ?? new GeminiConfig();
        var openAiCompatible = config.OpenAiCompatible ?? new OpenAiCompatibleConfig();

        return new EffectiveAgentBridgeConfig(
            FirstNonBlank(
                overrides.ModelProvider,
                Environment.GetEnvironmentVariable("AGENTBRIDGE_MODEL_PROVIDER"),
                config.ModelProvider,
                AgentBridgeDefaults.DefaultModelProvider),
            FirstNonBlank(
                overrides.DefaultExecutor,
                Environment.GetEnvironmentVariable("AGENTBRIDGE_DEFAULT_EXECUTOR"),
                Environment.GetEnvironmentVariable("AGENTBRIDGE_EXECUTOR"),
                config.DefaultExecutor,
                AgentBridgeDefaults.DefaultExecutor),
            FirstInt(
                overrides.MaxIterations,
                Environment.GetEnvironmentVariable("AGENTBRIDGE_MAX_ITERATIONS"),
                config.MaxIterations,
                AgentBridgeDefaults.DefaultMaxIterations),
            FirstBool(
                overrides.LogModelTraffic,
                Environment.GetEnvironmentVariable("AGENTBRIDGE_LOG_MODEL_TRAFFIC"),
                config.LogModelTraffic,
                false),
            new EffectiveGeminiConfig(
                FirstNonBlank(
                    overrides.GeminiModel,
                    Environment.GetEnvironmentVariable("AGENTBRIDGE_GEMINI_MODEL"),
                    gemini.Model,
                    AgentBridgeDefaults.DefaultGeminiModel),
                FirstNonBlankOrNull(
                    overrides.GeminiEndpoint,
                    Environment.GetEnvironmentVariable("AGENTBRIDGE_GEMINI_ENDPOINT"),
                    gemini.Endpoint),
                FirstNonBlankOrNull(
                    overrides.GeminiApiKey,
                    Environment.GetEnvironmentVariable("AGENTBRIDGE_GEMINI_API_KEY"),
                    gemini.ApiKey)),
            new EffectiveOpenAiCompatibleConfig(
                FirstNonBlank(
                    overrides.OpenAiCompatibleModel,
                    Environment.GetEnvironmentVariable("AGENTBRIDGE_OPENAI_MODEL"),
                    Environment.GetEnvironmentVariable("AGENTBRIDGE_STARK_MODEL"),
                    openAiCompatible.Model,
                    AgentBridgeDefaults.DefaultOpenAiCompatibleModel),
                FirstNonBlankOrNull(
                    overrides.OpenAiCompatibleEndpoint,
                    Environment.GetEnvironmentVariable("AGENTBRIDGE_OPENAI_ENDPOINT"),
                    Environment.GetEnvironmentVariable("AGENTBRIDGE_STARK_ENDPOINT"),
                    openAiCompatible.Endpoint),
                FirstNonBlankOrNull(
                    overrides.OpenAiCompatibleApiKey,
                    Environment.GetEnvironmentVariable("AGENTBRIDGE_OPENAI_API_KEY"),
                    Environment.GetEnvironmentVariable("AGENTBRIDGE_STARK_API_KEY"),
                    openAiCompatible.ApiKey),
                FirstNonBlank(
                    overrides.OpenAiCompatibleApiKeyHeader,
                    Environment.GetEnvironmentVariable("AGENTBRIDGE_OPENAI_API_KEY_HEADER"),
                    Environment.GetEnvironmentVariable("AGENTBRIDGE_STARK_API_KEY_HEADER"),
                    openAiCompatible.ApiKeyHeader,
                    AgentBridgeDefaults.DefaultOpenAiCompatibleApiKeyHeader),
                FirstValueOrDefault(
                    AgentBridgeDefaults.DefaultOpenAiCompatibleApiKeyPrefix,
                    overrides.OpenAiCompatibleApiKeyPrefix,
                    Environment.GetEnvironmentVariable("AGENTBRIDGE_OPENAI_API_KEY_PREFIX"),
                    Environment.GetEnvironmentVariable("AGENTBRIDGE_STARK_API_KEY_PREFIX"),
                    openAiCompatible.ApiKeyPrefix),
                FirstFloatOrNull(
                    Environment.GetEnvironmentVariable("AGENTBRIDGE_OPENAI_TEMPERATURE"),
                    Environment.GetEnvironmentVariable("AGENTBRIDGE_STARK_TEMPERATURE"),
                    openAiCompatible.Temperature),
                FirstIntOrNull(
                    Environment.GetEnvironmentVariable("AGENTBRIDGE_OPENAI_MAX_TOKENS"),
                    Environment.GetEnvironmentVariable("AGENTBRIDGE_STARK_MAX_TOKENS"),
                    openAiCompatible.MaxTokens),
                FirstIntOrNull(
                    Environment.GetEnvironmentVariable("AGENTBRIDGE_HTTP_TIMEOUT_SECONDS"),
                    Environment.GetEnvironmentVariable("AGENTBRIDGE_OPENAI_TIMEOUT_SECONDS"),
                    Environment.GetEnvironmentVariable("AGENTBRIDGE_STARK_TIMEOUT_SECONDS"),
                    openAiCompatible.RequestTimeoutSeconds)));
    }

    private static string FirstNonBlank(params string?[] values)
    {
        return values.First(value => !string.IsNullOrWhiteSpace(value))!;
    }

    private static string? FirstNonBlankOrNull(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static int FirstInt(int? overrideValue, string? environmentValue, int? configValue, int defaultValue)
    {
        if (overrideValue is not null)
        {
            return overrideValue.Value;
        }

        if (int.TryParse(environmentValue, out var parsed))
        {
            return parsed;
        }

        return configValue ?? defaultValue;
    }

    private static int? FirstIntOrNull(string? firstEnvironmentValue, string? secondEnvironmentValue, int? configValue)
    {
        if (int.TryParse(firstEnvironmentValue, out var parsed)
            || int.TryParse(secondEnvironmentValue, out parsed))
        {
            return parsed;
        }

        return configValue;
    }

    private static int? FirstIntOrNull(
        string? firstEnvironmentValue,
        string? secondEnvironmentValue,
        string? thirdEnvironmentValue,
        int? configValue)
    {
        if (int.TryParse(firstEnvironmentValue, out var parsed)
            || int.TryParse(secondEnvironmentValue, out parsed)
            || int.TryParse(thirdEnvironmentValue, out parsed))
        {
            return parsed;
        }

        return configValue;
    }

    private static float? FirstFloatOrNull(string? firstEnvironmentValue, string? secondEnvironmentValue, float? configValue)
    {
        if (float.TryParse(firstEnvironmentValue, out var parsed)
            || float.TryParse(secondEnvironmentValue, out parsed))
        {
            return parsed;
        }

        return configValue;
    }

    private static string? FirstValueOrDefault(string defaultValue, params string?[] values)
    {
        return values.FirstOrDefault(value => value is not null) ?? defaultValue;
    }

    private static bool FirstBool(bool? overrideValue, string? environmentValue, bool? configValue, bool defaultValue)
    {
        if (overrideValue is not null)
        {
            return overrideValue.Value;
        }

        if (bool.TryParse(environmentValue, out var parsed))
        {
            return parsed;
        }

        return configValue ?? defaultValue;
    }
}
