using OpenContext.AgentBridge.Core;
using OpenContext.AgentBridge.Core.Agents;
using OpenContext.AgentBridge.Core.Conversation;
using OpenContext.AgentBridge.Core.Execution;
using OpenContext.AgentBridge.Core.Skills;
using OpenContext.AgentBridge.Core.Tools;
using OpenContext.AgentBridge.Core.Tools.BuiltIn;
using OpenContext.AgentBridge.Core.Workspaces;
using OpenContext.AgentBridge.Providers.Gemini;
using OpenContext.AgentBridge.Storage;

return await ProgramMain.RunAsync(args);

internal static class ProgramMain
{
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

        var executor = CreateExecutor(parsed.Executor);
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
        var executor = CreateExecutor(parsed.Executor);
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
            Console.Error.WriteLine("Usage: agentbridge ask <workspace> [--executor host|docker] [--max-iterations n] <message>");
            return 1;
        }

        var options = AskOptions.Parse(args);
        var workspace = WorkspaceContext.FromPath(options.WorkspacePath);
        workspace.EnsureLocalState();

        var store = new SqliteConversationStore(workspace.ConversationDatabasePath);
        await store.InitializeAsync();

        var conversation = (await store.ListConversationsAsync(workspace.RootPath)).FirstOrDefault();
        var conversationId = conversation?.Id ?? await store.CreateConversationAsync(workspace.RootPath);

        await store.AppendMessageAsync(
            conversationId,
            new AgentMessage("user", options.Message, DateTimeOffset.UtcNow));

        var skills = await LoadSkillsAsync(workspace);
        var executor = CreateExecutor(options.Executor);

        using var httpClient = new HttpClient();
        var provider = new GeminiModelProvider(
            httpClient,
            new GeminiOptions
            {
                ApiKey = Environment.GetEnvironmentVariable("AGENTBRIDGE_GEMINI_API_KEY"),
                Endpoint = Environment.GetEnvironmentVariable("AGENTBRIDGE_GEMINI_ENDPOINT"),
                Model = Environment.GetEnvironmentVariable("AGENTBRIDGE_GEMINI_MODEL") ?? "gemini-1.5-pro"
            });

        var loop = new AgentLoop(
            provider,
            store,
            new ToolRegistry(BuiltInTools.CreateDefault()));
        var result = await loop.RunAsync(
            conversationId,
            workspace,
            executor,
            skills,
            new AgentLoopOptions(options.MaxIterations));

        Console.WriteLine(result.FinalMessage);
        return 0;
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
        var loader = new SkillLoader();
        return await loader.LoadAsync(new[]
        {
            workspace.SkillsPath,
            Path.Combine(workspace.RootPath, AgentBridgeDefaults.SkillsDirectoryName)
        });
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        Console.Error.WriteLine();
        WriteHelp();
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
              agentbridge ask <workspace> [--executor host|docker] [--max-iterations n] <message>

            Environment:
              AGENTBRIDGE_GEMINI_API_KEY
              AGENTBRIDGE_GEMINI_ENDPOINT
              AGENTBRIDGE_GEMINI_MODEL
              AGENTBRIDGE_DOCKER_IMAGE
            """);
    }

    private sealed record CommandOptions(
        string[] Positionals,
        string Executor,
        int TimeoutMinutes)
    {
        public static CommandOptions Parse(string[] args)
        {
            var positionals = new List<string>();
            var executor = "host";
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
        string Executor,
        int MaxIterations,
        string Message)
    {
        public static AskOptions Parse(string[] args)
        {
            var workspacePath = args[0];
            var executor = "host";
            var maxIterations = 8;
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
                    default:
                        messageParts.Add(args[index]);
                        break;
                }
            }

            if (messageParts.Count == 0)
            {
                throw new ArgumentException("An ask message is required.");
            }

            return new AskOptions(workspacePath, executor, maxIterations, string.Join(' ', messageParts));
        }
    }
}
