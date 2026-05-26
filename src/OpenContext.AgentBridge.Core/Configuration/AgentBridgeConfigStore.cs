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

        return new EffectiveAgentBridgeConfig(
            FirstNonBlank(
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
                    gemini.ApiKey)));
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
