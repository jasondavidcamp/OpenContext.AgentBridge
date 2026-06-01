using System.Diagnostics;
using System.Text.Json;
using OpenContext.AgentBridge.Core;
using OpenContext.AgentBridge.Core.Agents;
using OpenContext.AgentBridge.Core.Configuration;
using OpenContext.AgentBridge.Core.Conversation;
using OpenContext.AgentBridge.Core.Execution;
using OpenContext.AgentBridge.Core.Models;
using OpenContext.AgentBridge.Core.Skills;
using OpenContext.AgentBridge.Core.Tools;
using OpenContext.AgentBridge.Core.Tools.BuiltIn;
using OpenContext.AgentBridge.Core.Workspaces;
using OpenContext.AgentBridge.Providers.Gemini;
using OpenContext.AgentBridge.Providers.OpenAiCompatible;
using OpenContext.AgentBridge.Storage;

return await ProgramMain.RunAsync(args);

internal static class ProgramMain
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            WriteHelp();
            return 0;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "init" => await InitAsync(args[1..]),
                "doctor" => await DoctorAsync(args[1..]),
                "run" => await RunCommandAsync(args[1..]),
                "skills" => await ListSkillsAsync(args[1..]),
                "ask" => await AskAsync(args[1..]),
                "conversation" or "conversations" => await ConversationsAsync(args[1..]),
                "config" => await ConfigAsync(args[1..]),
                "model" or "models" => await ModelsAsync(args[1..]),
                _ => UnknownCommand(args[0])
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"agentbridge: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> InitAsync(string[] args)
    {
        var workspace = GetWorkspace(args);
        workspace.EnsureLocalState();

        var store = new SqliteConversationStore(workspace.ConversationDatabasePath);
        await store.InitializeAsync();

        Console.WriteLine($"Initialized AgentBridge workspace: {workspace.RootPath}");
        Console.WriteLine($"Local state: {workspace.LocalStatePath}");
        return 0;
    }

    private static async Task<int> DoctorAsync(string[] args)
    {
        var parsed = CommandOptions.Parse(args);
        var workspace = GetWorkspace(parsed.Positionals);
        workspace.EnsureLocalState();

        var store = new SqliteConversationStore(workspace.ConversationDatabasePath);
        await store.InitializeAsync();

        var config = await ReadEffectiveConfigAsync(
            workspace,
            new AgentBridgeConfigOverrides(DefaultExecutor: parsed.Executor));
        var executor = CreateExecutor(config.DefaultExecutor);
        var skills = await LoadSkillsAsync(workspace);

        Console.WriteLine($"Workspace: {workspace.RootPath}");
        Console.WriteLine($"Executor: {executor.Name}");
        Console.WriteLine($"Conversation database: {workspace.ConversationDatabasePath}");
        Console.WriteLine($"Loaded skills: {skills.Count}");
        Console.WriteLine();

        var gitResult = await executor.RunAsync(
            workspace,
            ShellCommand.Create("git status --short", TimeSpan.FromSeconds(30)));

        Console.WriteLine("Git status:");
        Console.Write(gitResult.StandardOutput);

        if (!string.IsNullOrWhiteSpace(gitResult.StandardError))
        {
            Console.Error.Write(gitResult.StandardError);
        }

        return gitResult.ExitCode == 0 ? 0 : gitResult.ExitCode;
    }

    private static async Task<int> RunCommandAsync(string[] args)
    {
        var separatorIndex = Array.IndexOf(args, "--");
        if (separatorIndex < 0 || separatorIndex == args.Length - 1)
        {
            Console.Error.WriteLine("Usage: agentbridge run [workspace] [--executor host|docker] -- <command>");
            return 1;
        }

        var parsed = CommandOptions.Parse(args[..separatorIndex]);
        var workspace = GetWorkspace(parsed.Positionals);
        var config = await ReadEffectiveConfigAsync(
            workspace,
            new AgentBridgeConfigOverrides(DefaultExecutor: parsed.Executor));
        var executor = CreateExecutor(config.DefaultExecutor);
        var command = string.Join(' ', args[(separatorIndex + 1)..]);

        var result = await executor.RunAsync(
            workspace,
            ShellCommand.Create(command, executor.Name, TimeSpan.FromMinutes(parsed.TimeoutMinutes)));

        Console.Write(result.StandardOutput);

        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            Console.Error.Write(result.StandardError);
        }

        if (result.TimedOut)
        {
            Console.Error.WriteLine($"Command timed out after {result.Duration}.");
        }

        return result.ExitCode;
    }

    private static async Task<int> ListSkillsAsync(string[] args)
    {
        var workspace = GetWorkspace(args);
        var skills = await LoadSkillsAsync(workspace);

        if (skills.Count == 0)
        {
            Console.WriteLine("No skills loaded.");
            return 0;
        }

        foreach (var skill in skills)
        {
            Console.WriteLine($"{skill.Name} - {skill.Path}");
        }

        return 0;
    }

    private static async Task<int> AskAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: agentbridge ask <workspace> [--new|--conversation id] [--executor host|docker] [--max-iterations n] [--require-tool-calls n] [--skill name|--skills names] <message>");
            return 1;
        }

        var options = AskOptions.Parse(args);
        var workspace = WorkspaceContext.FromPath(options.WorkspacePath);
        workspace.EnsureLocalState();

        var store = new SqliteConversationStore(workspace.ConversationDatabasePath);
        await store.InitializeAsync();

        var conversationId = await ResolveConversationIdAsync(store, workspace, options);

        await store.AppendMessageAsync(
            conversationId,
            new AgentMessage("user", options.Message, DateTimeOffset.UtcNow));

        var config = await ReadEffectiveConfigAsync(
            workspace,
            new AgentBridgeConfigOverrides(
                DefaultExecutor: options.Executor,
                MaxIterations: options.MaxIterations));
        var skills = await LoadSkillsAsync(workspace, options.SkillNames);
        var executor = CreateExecutor(config.DefaultExecutor);

        using var httpClient = CreateHttpClient(config);
        var provider = CreateModelProvider(httpClient, workspace, config);

        var loop = new AgentLoop(
            provider,
            store,
            new ToolRegistry(BuiltInTools.CreateDefault()));
        Console.WriteLine($"Conversation: {conversationId}");
        Console.WriteLine($"Workspace: {workspace.RootPath}");
        Console.WriteLine($"Executor: {executor.Name}");
        Console.WriteLine($"Skills: {FormatSkills(skills)}");
        Console.WriteLine();

        var result = await loop.RunAsync(
            conversationId,
            workspace,
            executor,
            skills,
            new AgentLoopOptions(
                config.MaxIterations,
                new ConsoleAgentProgress(),
                RequiredToolCallsBeforeFinal: options.RequiredToolCallsBeforeFinal));

        Console.WriteLine();
        Console.WriteLine("Final:");
        Console.WriteLine(result.FinalMessage);
        Console.WriteLine();

        await WriteRunSummaryAsync(workspace, executor, conversationId, result);
        return 0;
    }

    private static async Task<int> ConversationsAsync(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            WriteConversationsHelp();
            return 0;
        }

        return args[0].ToLowerInvariant() switch
        {
            "list" => await ListConversationsAsync(args[1..]),
            "show" => await ShowConversationAsync(args[1..]),
            _ => UnknownConversationsCommand(args[0])
        };
    }

    private static async Task<int> ListConversationsAsync(string[] args)
    {
        var workspace = GetWorkspace(args);
        workspace.EnsureLocalState();

        var store = new SqliteConversationStore(workspace.ConversationDatabasePath);
        await store.InitializeAsync();

        var conversations = await store.ListConversationsAsync(workspace.RootPath);
        if (conversations.Count == 0)
        {
            Console.WriteLine("No conversations found.");
            return 0;
        }

        foreach (var conversation in conversations)
        {
            Console.WriteLine($"{conversation.Id}  updated {conversation.UpdatedAt.LocalDateTime:g}  created {conversation.CreatedAt.LocalDateTime:g}");
        }

        return 0;
    }

    private static async Task<int> ShowConversationAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: agentbridge conversations show [workspace] <conversation-id>");
            return 1;
        }

        var workspace = args.Length == 1
            ? WorkspaceContext.FromPath(Directory.GetCurrentDirectory())
            : WorkspaceContext.FromPath(args[0]);
        var conversationId = args.Length == 1
            ? args[0]
            : args[1];
        workspace.EnsureLocalState();

        var store = new SqliteConversationStore(workspace.ConversationDatabasePath);
        await store.InitializeAsync();

        var conversation = (await store.ListConversationsAsync(workspace.RootPath))
            .FirstOrDefault(summary => string.Equals(summary.Id, conversationId, StringComparison.OrdinalIgnoreCase));
        if (conversation is null)
        {
            Console.Error.WriteLine($"Conversation not found in workspace: {conversationId}");
            return 1;
        }

        var messages = await store.ReadMessagesAsync(conversation.Id);
        var toolCalls = await store.ReadToolCallsAsync(conversation.Id);

        Console.WriteLine($"Conversation: {conversation.Id}");
        Console.WriteLine($"Workspace: {conversation.WorkspaceRoot}");
        Console.WriteLine($"Created: {conversation.CreatedAt.LocalDateTime:g}");
        Console.WriteLine($"Updated: {conversation.UpdatedAt.LocalDateTime:g}");
        Console.WriteLine();

        Console.WriteLine("Messages:");
        foreach (var message in messages)
        {
            Console.WriteLine($"[{message.CreatedAt.LocalDateTime:g}] {message.Role}");
            Console.WriteLine(Preview(message.Content, 1_200));
            Console.WriteLine();
        }

        Console.WriteLine("Tool Calls:");
        if (toolCalls.Count == 0)
        {
            Console.WriteLine("No tool calls.");
            return 0;
        }

        foreach (var toolCall in toolCalls)
        {
            Console.WriteLine($"[{toolCall.CreatedAt.LocalDateTime:g}] {toolCall.ToolName} {(toolCall.IsSuccess ? "ok" : "failed")}");
            Console.WriteLine($"Arguments: {SummarizeArguments(toolCall.ToolName, toolCall.ArgumentsJson)}");
            Console.WriteLine(Preview(toolCall.ResultContent, 800));
            Console.WriteLine();
        }

        return 0;
    }

    private static async Task<int> ConfigAsync(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            WriteConfigHelp();
            return 0;
        }

        return args[0].ToLowerInvariant() switch
        {
            "init" => await InitConfigAsync(args[1..]),
            "show" => await ShowConfigAsync(args[1..]),
            _ => UnknownConfigCommand(args[0])
        };
    }

    private static async Task<int> InitConfigAsync(string[] args)
    {
        var force = args.Any(argument => string.Equals(argument, "--force", StringComparison.OrdinalIgnoreCase));
        var workspace = GetWorkspace(args.Where(argument => !string.Equals(argument, "--force", StringComparison.OrdinalIgnoreCase)).ToArray());
        var store = new AgentBridgeConfigStore();
        var created = await store.WriteDefaultAsync(workspace, overwrite: force);

        Console.WriteLine(created
            ? $"Created config: {workspace.ConfigPath}"
            : $"Config already exists: {workspace.ConfigPath}");

        if (!created && !force)
        {
            Console.WriteLine("Use --force to overwrite it.");
        }

        return 0;
    }

    private static async Task<int> ShowConfigAsync(string[] args)
    {
        var workspace = GetWorkspace(args);
        workspace.EnsureLocalState();

        var config = await ReadEffectiveConfigAsync(workspace);

        Console.WriteLine($"Config file: {workspace.ConfigPath}");
        Console.WriteLine(JsonSerializer.Serialize(ToRedactedConfig(config), JsonOptions));
        return 0;
    }

    private static async Task<int> ModelsAsync(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            WriteModelsHelp();
            return 0;
        }

        return args[0].ToLowerInvariant() switch
        {
            "list" => await ListModelsAsync(args[1..]),
            "test" => await TestModelAsync(args[1..]),
            _ => UnknownModelsCommand(args[0])
        };
    }

    private static async Task<int> ListModelsAsync(string[] args)
    {
        var options = ModelListOptions.Parse(args);
        var workspace = WorkspaceContext.FromPath(options.WorkspacePath);
        workspace.EnsureLocalState();

        var config = await ReadEffectiveConfigAsync(
            workspace,
            new AgentBridgeConfigOverrides(
                ModelProvider: options.Provider,
                OpenAiCompatibleEndpoint: options.Endpoint,
                OpenAiCompatibleApiKey: options.ApiKey,
                OpenAiCompatibleApiKeyHeader: options.ApiKeyHeader,
                OpenAiCompatibleApiKeyPrefix: options.ApiKeyPrefix));

        if (!string.Equals(NormalizeProviderName(config.ModelProvider), "openai-compatible", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Model listing is currently available for openai-compatible or stark providers.");
            return 1;
        }

        using var httpClient = CreateHttpClient(config);
        var client = new OpenAiCompatibleModelCatalogClient(
            httpClient,
            CreateOpenAiCompatibleOptions(workspace, config));

        Console.WriteLine($"Provider: {config.ModelProvider}");
        Console.WriteLine($"Endpoint: {GetRedactedModelListEndpoint(workspace, config)}");

        try
        {
            var models = await client.ListAsync();
            if (models.Count == 0)
            {
                Console.WriteLine("No models returned.");
                return 0;
            }

            foreach (var model in models)
            {
                var created = model.Created <= 0
                    ? string.Empty
                    : $" created {DateTimeOffset.FromUnixTimeSeconds(model.Created).LocalDateTime:g}";
                var ownedBy = string.IsNullOrWhiteSpace(model.OwnedBy)
                    ? string.Empty
                    : $" owned by {model.OwnedBy}";
                Console.WriteLine($"{model.Id}{ownedBy}{created}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static async Task<int> TestModelAsync(string[] args)
    {
        var options = ModelTestOptions.Parse(args);
        var workspace = WorkspaceContext.FromPath(options.WorkspacePath);
        workspace.EnsureLocalState();

        var config = await ReadEffectiveConfigAsync(
            workspace,
            new AgentBridgeConfigOverrides(
                ModelProvider: options.Provider,
                GeminiEndpoint: options.Endpoint,
                GeminiModel: options.Model,
                GeminiApiKey: options.ApiKey,
                OpenAiCompatibleEndpoint: options.Endpoint,
                OpenAiCompatibleModel: options.Model,
                OpenAiCompatibleApiKey: options.ApiKey,
                OpenAiCompatibleApiKeyHeader: options.ApiKeyHeader,
                OpenAiCompatibleApiKeyPrefix: options.ApiKeyPrefix,
                LogModelTraffic: options.LogModelTraffic));

        using var httpClient = CreateHttpClient(config);
        var provider = CreateModelProvider(httpClient, workspace, config);

        Console.WriteLine($"Provider: {config.ModelProvider}");
        Console.WriteLine($"Model: {GetModelName(config)}");
        Console.WriteLine($"Endpoint: {GetRedactedModelEndpoint(workspace, config)}");
        Console.WriteLine($"Traffic logging: {config.LogModelTraffic}");

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await provider.CompleteAsync(
                new(
                    workspace.RootPath,
                    new[]
                    {
                        new AgentMessage(
                            "user",
                            options.Message,
                            DateTimeOffset.UtcNow)
                    },
                    Array.Empty<Skill>(),
                    Array.Empty<ToolDefinition>()));

            stopwatch.Stop();
            Console.WriteLine($"Status: ok");
            Console.WriteLine($"Latency: {stopwatch.Elapsed}");
            Console.WriteLine("Response preview:");
            Console.WriteLine(Preview(response.Content, 1_200));
            return 0;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Console.WriteLine("Status: failed");
            Console.WriteLine($"Latency: {stopwatch.Elapsed}");
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static WorkspaceContext GetWorkspace(string[] args)
    {
        var path = args.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal))
            ?? Directory.GetCurrentDirectory();

        return WorkspaceContext.FromPath(path);
    }

    private static IWorkspaceExecutor CreateExecutor(string executor)
    {
        return executor.ToLowerInvariant() switch
        {
            "host" => new HostWorkspaceExecutor(),
            "docker" => new DockerWorkspaceExecutor(
                Environment.GetEnvironmentVariable("AGENTBRIDGE_DOCKER_IMAGE")
                    ?? AgentBridgeDefaults.DefaultDockerImage),
            _ => throw new ArgumentException($"Unknown executor: {executor}")
        };
    }

    private static async Task<IReadOnlyList<Skill>> LoadSkillsAsync(WorkspaceContext workspace)
    {
        return await LoadSkillsAsync(workspace, Array.Empty<string>());
    }

    private static async Task<IReadOnlyList<Skill>> LoadSkillsAsync(
        WorkspaceContext workspace,
        IEnumerable<string> requestedSkills)
    {
        var loader = new SkillLoader();
        var skills = await loader.LoadAsync(new[]
        {
            workspace.SkillsPath,
            Path.Combine(workspace.RootPath, AgentBridgeDefaults.SkillsDirectoryName)
        });

        return SkillSelector.Select(skills, requestedSkills);
    }

    private static Task<EffectiveAgentBridgeConfig> ReadEffectiveConfigAsync(
        WorkspaceContext workspace,
        AgentBridgeConfigOverrides? overrides = null)
    {
        return new AgentBridgeConfigStore().ReadEffectiveAsync(workspace, overrides);
    }

    private static IModelProvider CreateModelProvider(
        HttpClient httpClient,
        WorkspaceContext workspace,
        EffectiveAgentBridgeConfig config)
    {
        return NormalizeProviderName(config.ModelProvider) switch
        {
            "gemini" => new GeminiModelProvider(httpClient, CreateGeminiOptions(workspace, config)),
            "openai-compatible" => new OpenAiCompatibleModelProvider(
                httpClient,
                CreateOpenAiCompatibleOptions(workspace, config)),
            _ => throw new ArgumentException($"Unknown model provider: {config.ModelProvider}")
        };
    }

    private static HttpClient CreateHttpClient(EffectiveAgentBridgeConfig config)
    {
        var httpClient = new HttpClient();
        if (string.Equals(NormalizeProviderName(config.ModelProvider), "openai-compatible", StringComparison.Ordinal)
            && config.OpenAiCompatible.RequestTimeoutSeconds is > 0 and <= 3_600)
        {
            httpClient.Timeout = TimeSpan.FromSeconds(config.OpenAiCompatible.RequestTimeoutSeconds.Value);
        }

        return httpClient;
    }

    private static GeminiOptions CreateGeminiOptions(
        WorkspaceContext workspace,
        EffectiveAgentBridgeConfig config)
    {
        return new GeminiOptions
        {
            ApiKey = config.Gemini.ApiKey,
            Endpoint = config.Gemini.Endpoint,
            Model = config.Gemini.Model,
            LogModelTraffic = config.LogModelTraffic,
            LogDirectory = workspace.LogsPath
        };
    }

    private static OpenAiCompatibleOptions CreateOpenAiCompatibleOptions(
        WorkspaceContext workspace,
        EffectiveAgentBridgeConfig config)
    {
        return new OpenAiCompatibleOptions
        {
            ApiKey = config.OpenAiCompatible.ApiKey,
            ApiKeyHeader = config.OpenAiCompatible.ApiKeyHeader,
            ApiKeyPrefix = config.OpenAiCompatible.ApiKeyPrefix,
            Endpoint = config.OpenAiCompatible.Endpoint,
            Model = config.OpenAiCompatible.Model,
            Temperature = config.OpenAiCompatible.Temperature,
            MaxTokens = config.OpenAiCompatible.MaxTokens,
            LogModelTraffic = config.LogModelTraffic,
            LogDirectory = workspace.LogsPath
        };
    }

    private static string NormalizeProviderName(string provider)
    {
        return provider.ToLowerInvariant() switch
        {
            "openai" or "openai-compatible" or "stark" or "gemini-openai" or "gemini-openai-compatible" => "openai-compatible",
            var normalized => normalized
        };
    }

    private static string GetModelName(EffectiveAgentBridgeConfig config)
    {
        return NormalizeProviderName(config.ModelProvider) switch
        {
            "gemini" => config.Gemini.Model,
            "openai-compatible" => config.OpenAiCompatible.Model,
            _ => "<unknown>"
        };
    }

    private static string GetRedactedModelEndpoint(WorkspaceContext workspace, EffectiveAgentBridgeConfig config)
    {
        try
        {
            return NormalizeProviderName(config.ModelProvider) switch
            {
                "gemini" => CreateGeminiOptions(workspace, config).GetRedactedEndpoint(),
                "openai-compatible" => CreateOpenAiCompatibleOptions(workspace, config).GetRedactedEndpoint(),
                _ => "<unknown>"
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or UriFormatException)
        {
            return "<not configured>";
        }
    }

    private static string GetRedactedModelListEndpoint(WorkspaceContext workspace, EffectiveAgentBridgeConfig config)
    {
        try
        {
            return OpenAiCompatibleOptions.RedactEndpoint(
                CreateOpenAiCompatibleOptions(workspace, config).GetModelsEndpoint());
        }
        catch (Exception ex) when (ex is InvalidOperationException or UriFormatException)
        {
            return "<not configured>";
        }
    }

    private static string FormatSkills(IReadOnlyList<Skill> skills)
    {
        return skills.Count == 0
            ? "none"
            : string.Join(", ", skills.Select(skill => skill.Name));
    }

    private static async Task<string> ResolveConversationIdAsync(
        IConversationStore store,
        WorkspaceContext workspace,
        AskOptions options)
    {
        if (options.StartNew && !string.IsNullOrWhiteSpace(options.ConversationId))
        {
            throw new ArgumentException("Use either --new or --conversation, not both.");
        }

        if (options.StartNew)
        {
            return await store.CreateConversationAsync(workspace.RootPath);
        }

        var conversations = await store.ListConversationsAsync(workspace.RootPath);

        if (!string.IsNullOrWhiteSpace(options.ConversationId))
        {
            var existing = conversations.FirstOrDefault(conversation =>
                string.Equals(conversation.Id, options.ConversationId, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                throw new ArgumentException($"Conversation not found in workspace: {options.ConversationId}");
            }

            return existing.Id;
        }

        return conversations.FirstOrDefault()?.Id
            ?? await store.CreateConversationAsync(workspace.RootPath);
    }

    private static async Task WriteRunSummaryAsync(
        WorkspaceContext workspace,
        IWorkspaceExecutor executor,
        string conversationId,
        AgentLoopResult result)
    {
        var succeeded = result.ToolCalls.Count(toolCall => toolCall.IsSuccess);
        var failed = result.ToolCalls.Count - succeeded;

        Console.WriteLine("Run Summary:");
        Console.WriteLine($"Conversation: {conversationId}");
        Console.WriteLine($"Turns: {result.Turns}");
        Console.WriteLine($"Stopped because: {result.StoppedBecause}");
        Console.WriteLine($"Tool calls: {result.ToolCalls.Count} ({succeeded} ok, {failed} failed)");
        WriteToolBreakdown(result.ToolCalls);

        var commands = result.ToolCalls
            .Where(toolCall => string.Equals(toolCall.ToolName, "run_command", StringComparison.OrdinalIgnoreCase))
            .Select(CreateCommandSummary)
            .ToArray();
        if (commands.Length > 0)
        {
            Console.WriteLine("Commands run:");
            foreach (var command in commands)
            {
                Console.WriteLine($"  {command.Status} {command.Command}");
            }

            var validationCommands = commands
                .Where(command => command.IsValidation)
                .ToArray();
            if (validationCommands.Length > 0)
            {
                Console.WriteLine("Validation:");
                foreach (var command in validationCommands)
                {
                    Console.WriteLine($"  {command.Status} {command.Command}");
                }
            }
        }

        var gitStatus = await executor.RunAsync(
            workspace,
            CommandRequest.Create("git", new[] { "status", "--short", "--", "." }, TimeSpan.FromSeconds(30)));
        if (gitStatus.ExitCode == 0 && !string.IsNullOrWhiteSpace(gitStatus.StandardOutput))
        {
            Console.WriteLine("Changed files:");
            foreach (var line in gitStatus.StandardOutput.Split(
                         Environment.NewLine,
                         StringSplitOptions.RemoveEmptyEntries))
            {
                Console.WriteLine($"  {line}");
            }
        }
        else if (gitStatus.ExitCode == 0)
        {
            Console.WriteLine("Changed files: none");
        }
    }

    private static void WriteToolBreakdown(IReadOnlyList<ToolCallRecord> toolCalls)
    {
        if (toolCalls.Count == 0)
        {
            return;
        }

        Console.WriteLine("Tool breakdown:");
        foreach (var group in toolCalls
                     .GroupBy(toolCall => toolCall.ToolName, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var ok = group.Count(toolCall => toolCall.IsSuccess);
            var failed = group.Count() - ok;
            Console.WriteLine($"  {group.Key}: {group.Count()} ({ok} ok, {failed} failed)");
        }
    }

    private static CommandSummary CreateCommandSummary(ToolCallRecord toolCall)
    {
        var command = SummarizeArguments(toolCall.ToolName, toolCall.ArgumentsJson);
        var exitCode = ExtractExitCode(toolCall.ResultContent);
        var status = toolCall.IsSuccess
            ? "[ok]"
            : "[failed]";

        if (exitCode is not null)
        {
            status += $" exit {exitCode}";
        }

        return new CommandSummary(
            command,
            status,
            IsValidationCommand(command));
    }

    private static int? ExtractExitCode(string content)
    {
        using var reader = new StringReader(content);
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith("Exit code:", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line["Exit code:".Length..].Trim(), out var exitCode))
            {
                return exitCode;
            }
        }

        return null;
    }

    private static bool IsValidationCommand(string command)
    {
        var normalized = command.ToLowerInvariant();
        string[] indicators =
        {
            "dotnet test",
            "dotnet build",
            "dotnet run",
            "invoke-pester",
            "invoke-scriptanalyzer",
            "pwsh ",
            "powershell "
        };

        return indicators.Any(normalized.Contains);
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        Console.Error.WriteLine();
        WriteHelp();
        return 1;
    }

    private static int UnknownConversationsCommand(string command)
    {
        Console.Error.WriteLine($"Unknown conversations command: {command}");
        Console.Error.WriteLine();
        WriteConversationsHelp();
        return 1;
    }

    private static int UnknownConfigCommand(string command)
    {
        Console.Error.WriteLine($"Unknown config command: {command}");
        Console.Error.WriteLine();
        WriteConfigHelp();
        return 1;
    }

    private static int UnknownModelsCommand(string command)
    {
        Console.Error.WriteLine($"Unknown models command: {command}");
        Console.Error.WriteLine();
        WriteModelsHelp();
        return 1;
    }

    private static bool IsHelp(string value)
    {
        return value is "-h" or "--help" or "help";
    }

    private static void WriteHelp()
    {
        Console.WriteLine("""
            AgentBridge

            Usage:
              agentbridge init [workspace]
              agentbridge doctor [workspace] [--executor host|docker]
              agentbridge run [workspace] [--executor host|docker] [--timeout-minutes n] -- <command>
              agentbridge skills [workspace]
              agentbridge ask <workspace> [--new|--conversation id] [--executor host|docker] [--max-iterations n] [--require-tool-calls n] [--skill name|--skills names] <message>
              agentbridge conversations list [workspace]
              agentbridge conversations show [workspace] <conversation-id>
              agentbridge config init [workspace] [--force]
              agentbridge config show [workspace]
              agentbridge models list [workspace] [--provider name] [--endpoint url]
              agentbridge models test [workspace] [--provider name] [--endpoint url] [--model name]

            Environment:
              AGENTBRIDGE_MODEL_PROVIDER
              AGENTBRIDGE_GEMINI_API_KEY
              AGENTBRIDGE_GEMINI_ENDPOINT
              AGENTBRIDGE_GEMINI_MODEL
              AGENTBRIDGE_OPENAI_API_KEY or AGENTBRIDGE_STARK_API_KEY
              AGENTBRIDGE_OPENAI_ENDPOINT or AGENTBRIDGE_STARK_ENDPOINT
              AGENTBRIDGE_OPENAI_MODEL or AGENTBRIDGE_STARK_MODEL
              AGENTBRIDGE_HTTP_TIMEOUT_SECONDS or AGENTBRIDGE_STARK_TIMEOUT_SECONDS
              AGENTBRIDGE_DEFAULT_EXECUTOR
              AGENTBRIDGE_MAX_ITERATIONS
              AGENTBRIDGE_LOG_MODEL_TRAFFIC
              AGENTBRIDGE_DOCKER_IMAGE
            """);
    }

    private static void WriteConversationsHelp()
    {
        Console.WriteLine("""
            AgentBridge Conversations

            Usage:
              agentbridge conversations list [workspace]
              agentbridge conversations show [workspace] <conversation-id>
            """);
    }

    private static void WriteConfigHelp()
    {
        Console.WriteLine("""
            AgentBridge Config

            Usage:
              agentbridge config init [workspace] [--force]
              agentbridge config show [workspace]
            """);
    }

    private static void WriteModelsHelp()
    {
        Console.WriteLine("""
            AgentBridge Models

            Usage:
              agentbridge models list [workspace] [--provider openai-compatible|stark|gemini-openai] [--endpoint url] [--api-key key] [--api-key-header name] [--api-key-prefix value]
              agentbridge models test [workspace] [--provider gemini|openai-compatible|stark|gemini-openai] [--endpoint url] [--model name] [--api-key key] [--api-key-header name] [--api-key-prefix value] [--message text] [--log-traffic]
            """);
    }

    private static object ToRedactedConfig(EffectiveAgentBridgeConfig config)
    {
        return new
        {
            config.ModelProvider,
            config.DefaultExecutor,
            config.MaxIterations,
            config.LogModelTraffic,
            Gemini = new
            {
                config.Gemini.Model,
                Endpoint = RedactEndpoint(config.Gemini.Endpoint),
                ApiKey = string.IsNullOrWhiteSpace(config.Gemini.ApiKey)
                    ? null
                    : "<redacted>"
            },
            OpenAiCompatible = new
            {
                config.OpenAiCompatible.Model,
                Endpoint = RedactEndpoint(config.OpenAiCompatible.Endpoint),
                ApiKey = string.IsNullOrWhiteSpace(config.OpenAiCompatible.ApiKey)
                    ? null
                    : "<redacted>",
                config.OpenAiCompatible.ApiKeyHeader,
                config.OpenAiCompatible.ApiKeyPrefix,
                config.OpenAiCompatible.Temperature,
                config.OpenAiCompatible.MaxTokens,
                config.OpenAiCompatible.RequestTimeoutSeconds
            }
        };
    }

    private static string? RedactEndpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            return endpoint;
        }

        return OpenAiCompatibleOptions.RedactEndpoint(uri);
    }

    private static string SummarizeArguments(string toolName, string argumentsJson)
    {
        try
        {
            using var document = JsonDocument.Parse(argumentsJson);
            var root = document.RootElement;

            if (string.Equals(toolName, "run_command", StringComparison.OrdinalIgnoreCase)
                && root.TryGetProperty("command", out var command))
            {
                return Preview(command.GetString() ?? string.Empty, 180);
            }

            if (root.TryGetProperty("path", out var path))
            {
                return path.GetString() ?? argumentsJson;
            }

            if (root.TryGetProperty("query", out var query))
            {
                return $"query: {Preview(query.GetString() ?? string.Empty, 120)}";
            }

            if (root.TryGetProperty("patch", out var patch))
            {
                return $"patch: {(patch.GetString() ?? string.Empty).Length} chars";
            }
        }
        catch (JsonException)
        {
            return Preview(argumentsJson, 180);
        }

        return Preview(argumentsJson, 180);
    }

    private static string Preview(string value, int maxCharacters = 400)
    {
        var preview = value.ReplaceLineEndings(" ").Trim();

        return preview.Length <= maxCharacters
            ? preview
            : preview[..maxCharacters] + "...";
    }

    private sealed record CommandSummary(
        string Command,
        string Status,
        bool IsValidation);

    private sealed record CommandOptions(
        string[] Positionals,
        string? Executor,
        int TimeoutMinutes)
    {
        public static CommandOptions Parse(string[] args)
        {
            var positionals = new List<string>();
            string? executor = null;
            var timeoutMinutes = 10;

            for (var index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--executor" when index + 1 < args.Length:
                        executor = args[++index];
                        break;
                    case "--timeout-minutes" when index + 1 < args.Length && int.TryParse(args[++index], out var timeout):
                        timeoutMinutes = timeout;
                        break;
                    default:
                        positionals.Add(args[index]);
                        break;
                }
            }

            return new CommandOptions(positionals.ToArray(), executor, timeoutMinutes);
        }
    }

    private sealed record AskOptions(
        string WorkspacePath,
        string? Executor,
        int? MaxIterations,
        int RequiredToolCallsBeforeFinal,
        bool StartNew,
        string? ConversationId,
        string[] SkillNames,
        string Message)
    {
        public static AskOptions Parse(string[] args)
        {
            var workspacePath = args[0];
            string? executor = null;
            int? maxIterations = null;
            var requiredToolCallsBeforeFinal = 0;
            var startNew = false;
            string? conversationId = null;
            var skillNames = new List<string>();
            var messageParts = new List<string>();

            for (var index = 1; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--executor" when index + 1 < args.Length:
                        executor = args[++index];
                        break;
                    case "--max-iterations" when index + 1 < args.Length && int.TryParse(args[++index], out var parsed):
                        maxIterations = Math.Clamp(parsed, 1, 20);
                        break;
                    case "--require-tool-calls" when index + 1 < args.Length && int.TryParse(args[++index], out var requiredToolCalls):
                        requiredToolCallsBeforeFinal = Math.Clamp(requiredToolCalls, 0, 20);
                        break;
                    case "--new":
                        startNew = true;
                        break;
                    case "--conversation" when index + 1 < args.Length:
                        conversationId = args[++index];
                        break;
                    case "--skill" when index + 1 < args.Length:
                    case "--skills" when index + 1 < args.Length:
                        skillNames.Add(args[++index]);
                        break;
                    default:
                        messageParts.Add(args[index]);
                        break;
                }
            }

            if (messageParts.Count == 0)
            {
                throw new ArgumentException("An ask message is required.");
            }

            return new AskOptions(
                workspacePath,
                executor,
                maxIterations,
                requiredToolCallsBeforeFinal,
                startNew,
                conversationId,
                skillNames.ToArray(),
                string.Join(' ', messageParts));
        }
    }

    private sealed record ModelTestOptions(
        string WorkspacePath,
        string? Provider,
        string? Endpoint,
        string? Model,
        string? ApiKey,
        string? ApiKeyHeader,
        string? ApiKeyPrefix,
        bool? LogModelTraffic,
        string Message)
    {
        public static ModelTestOptions Parse(string[] args)
        {
            var workspacePath = Directory.GetCurrentDirectory();
            string? provider = null;
            string? endpoint = null;
            string? model = null;
            string? apiKey = null;
            string? apiKeyHeader = null;
            string? apiKeyPrefix = null;
            bool? logModelTraffic = null;
            var message = """Return exactly {"type":"final","message":"model test ok"}.""";

            for (var index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--provider" when index + 1 < args.Length:
                        provider = args[++index];
                        break;
                    case "--endpoint" when index + 1 < args.Length:
                        endpoint = args[++index];
                        break;
                    case "--model" when index + 1 < args.Length:
                        model = args[++index];
                        break;
                    case "--api-key" when index + 1 < args.Length:
                        apiKey = args[++index];
                        break;
                    case "--api-key-header" when index + 1 < args.Length:
                        apiKeyHeader = args[++index];
                        break;
                    case "--api-key-prefix" when index + 1 < args.Length:
                        apiKeyPrefix = args[++index];
                        break;
                    case "--message" when index + 1 < args.Length:
                        message = args[++index];
                        break;
                    case "--log-traffic":
                        logModelTraffic = true;
                        break;
                    default:
                        if (!args[index].StartsWith("--", StringComparison.Ordinal))
                        {
                            workspacePath = args[index];
                        }

                        break;
                }
            }

            return new ModelTestOptions(
                workspacePath,
                provider,
                endpoint,
                model,
                apiKey,
                apiKeyHeader,
                apiKeyPrefix,
                logModelTraffic,
                message);
        }
    }

    private sealed record ModelListOptions(
        string WorkspacePath,
        string? Provider,
        string? Endpoint,
        string? ApiKey,
        string? ApiKeyHeader,
        string? ApiKeyPrefix)
    {
        public static ModelListOptions Parse(string[] args)
        {
            var workspacePath = Directory.GetCurrentDirectory();
            string? provider = null;
            string? endpoint = null;
            string? apiKey = null;
            string? apiKeyHeader = null;
            string? apiKeyPrefix = null;

            for (var index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--provider" when index + 1 < args.Length:
                        provider = args[++index];
                        break;
                    case "--endpoint" when index + 1 < args.Length:
                        endpoint = args[++index];
                        break;
                    case "--api-key" when index + 1 < args.Length:
                        apiKey = args[++index];
                        break;
                    case "--api-key-header" when index + 1 < args.Length:
                        apiKeyHeader = args[++index];
                        break;
                    case "--api-key-prefix" when index + 1 < args.Length:
                        apiKeyPrefix = args[++index];
                        break;
                    default:
                        if (!args[index].StartsWith("--", StringComparison.Ordinal))
                        {
                            workspacePath = args[index];
                        }

                        break;
                }
            }

            return new ModelListOptions(
                workspacePath,
                provider,
                endpoint,
                apiKey,
                apiKeyHeader,
                apiKeyPrefix);
        }
    }

    private sealed class ConsoleAgentProgress : IProgress<AgentProgressEvent>
    {
        public void Report(AgentProgressEvent value)
        {
            switch (value.Kind)
            {
                case AgentProgressKind.ModelRequest:
                    Console.WriteLine($"[turn {value.Turn}] thinking...");
                    break;
                case AgentProgressKind.InvalidModelResponse:
                    Console.WriteLine($"[turn {value.Turn}] invalid model response: {value.Preview}");
                    break;
                case AgentProgressKind.ToolRequested:
                    Console.WriteLine($"[turn {value.Turn}] tool {value.ToolName}: {SummarizeArguments(value.ToolName ?? string.Empty, value.ArgumentsJson ?? "{}")}");
                    break;
                case AgentProgressKind.ToolCompleted:
                    Console.WriteLine($"[turn {value.Turn}] tool {value.ToolName} {(value.IsSuccess == true ? "ok" : "failed")}: {value.Preview}");
                    break;
                case AgentProgressKind.MaxIterations:
                    Console.WriteLine($"[turn {value.Turn}] stopped: {value.Message}");
                    break;
            }
        }
    }
}
