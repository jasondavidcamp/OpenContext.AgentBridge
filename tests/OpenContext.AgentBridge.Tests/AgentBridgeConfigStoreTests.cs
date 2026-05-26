using OpenContext.AgentBridge.Core.Configuration;
using OpenContext.AgentBridge.Core.Workspaces;

namespace OpenContext.AgentBridge.Tests;

[Collection("Environment")]
public sealed class AgentBridgeConfigStoreTests
{
    [Fact]
    public async Task ReadEffectiveAsync_uses_file_values()
    {
        var root = CreateTempDirectory();
        using var environment = EnvironmentScope.ClearAgentBridgeVariables();

        try
        {
            var workspace = WorkspaceContext.FromPath(root);
            var store = new AgentBridgeConfigStore();
            await store.WriteAsync(
                workspace,
                new AgentBridgeConfig
                {
                    DefaultExecutor = "docker",
                    MaxIterations = 5,
                    LogModelTraffic = true,
                    Gemini = new GeminiConfig
                    {
                        Model = "gemini-test",
                        Endpoint = "https://example.test/gemini",
                        ApiKey = "file-key"
                    }
                });

            var config = await store.ReadEffectiveAsync(workspace);

            Assert.Equal("docker", config.DefaultExecutor);
            Assert.Equal(5, config.MaxIterations);
            Assert.True(config.LogModelTraffic);
            Assert.Equal("gemini-test", config.Gemini.Model);
            Assert.Equal("https://example.test/gemini", config.Gemini.Endpoint);
            Assert.Equal("file-key", config.Gemini.ApiKey);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReadEffectiveAsync_prefers_overrides_then_environment_then_file()
    {
        var root = CreateTempDirectory();
        using var environment = EnvironmentScope.ClearAgentBridgeVariables();

        try
        {
            Environment.SetEnvironmentVariable("AGENTBRIDGE_GEMINI_MODEL", "env-model");
            Environment.SetEnvironmentVariable("AGENTBRIDGE_DEFAULT_EXECUTOR", "docker");

            var workspace = WorkspaceContext.FromPath(root);
            var store = new AgentBridgeConfigStore();
            await store.WriteAsync(
                workspace,
                new AgentBridgeConfig
                {
                    DefaultExecutor = "host",
                    Gemini = new GeminiConfig
                    {
                        Model = "file-model"
                    }
                });

            var config = await store.ReadEffectiveAsync(
                workspace,
                new AgentBridgeConfigOverrides(DefaultExecutor: "host", GeminiModel: "override-model"));

            Assert.Equal("host", config.DefaultExecutor);
            Assert.Equal("override-model", config.Gemini.Model);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"agentbridge-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class EnvironmentScope : IDisposable
    {
        private static readonly string[] Names =
        {
            "AGENTBRIDGE_MODEL_PROVIDER",
            "AGENTBRIDGE_DEFAULT_EXECUTOR",
            "AGENTBRIDGE_EXECUTOR",
            "AGENTBRIDGE_MAX_ITERATIONS",
            "AGENTBRIDGE_LOG_MODEL_TRAFFIC",
            "AGENTBRIDGE_GEMINI_MODEL",
            "AGENTBRIDGE_GEMINI_ENDPOINT",
            "AGENTBRIDGE_GEMINI_API_KEY"
        };

        private readonly Dictionary<string, string?> _previous;

        private EnvironmentScope(Dictionary<string, string?> previous)
        {
            _previous = previous;
        }

        public static EnvironmentScope ClearAgentBridgeVariables()
        {
            var previous = Names.ToDictionary(name => name, Environment.GetEnvironmentVariable);
            foreach (var name in Names)
            {
                Environment.SetEnvironmentVariable(name, null);
            }

            return new EnvironmentScope(previous);
        }

        public void Dispose()
        {
            foreach (var pair in _previous)
            {
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }
    }
}

[CollectionDefinition("Environment", DisableParallelization = true)]
public sealed class EnvironmentCollection;
