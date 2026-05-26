namespace OpenContext.AgentBridge.Core.Tools.BuiltIn;

public sealed class ListFilesTool : IAgentTool
{
    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".agentbridge",
        ".git",
        ".vs",
        "bin",
        "obj"
    };

    public ToolDefinition Definition { get; } = new(
        "list_files",
        "List files and directories under a workspace path.",
        """{"path":"optional relative path","recursive":true,"max_results":200}""");

    public Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        AgentDirective directive,
        CancellationToken cancellationToken = default)
    {
        var path = ToolArguments.GetString(directive.Arguments, "path") ?? ".";
        var recursive = ToolArguments.GetBool(directive.Arguments, "recursive", true);
        var maxResults = ToolArguments.GetInt(directive.Arguments, "max_results", 200, 1, 1_000);
        var resolved = context.Workspace.ResolvePath(path);

        if (File.Exists(resolved))
        {
            return Task.FromResult(ToolResult.Success(ToRelativePath(context, resolved)));
        }

        if (!Directory.Exists(resolved))
        {
            return Task.FromResult(ToolResult.Failure($"Path does not exist: {path}"));
        }

        var results = new List<string>();
        Enumerate(context, resolved, recursive, maxResults, results);

        var suffix = results.Count >= maxResults
            ? $"{Environment.NewLine}[truncated at {maxResults} results]"
            : string.Empty;

        return Task.FromResult(ToolResult.Success(
            results.Count == 0
                ? "No files found."
                : string.Join(Environment.NewLine, results) + suffix));
    }

    private static void Enumerate(
        ToolExecutionContext context,
        string directory,
        bool recursive,
        int maxResults,
        List<string> results)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory).OrderBy(Path.GetFileName))
        {
            if (results.Count >= maxResults)
            {
                return;
            }

            var name = Path.GetFileName(entry);
            var isDirectory = Directory.Exists(entry);

            if (isDirectory && IgnoredDirectoryNames.Contains(name))
            {
                continue;
            }

            results.Add(ToRelativePath(context, entry) + (isDirectory ? "/" : string.Empty));

            if (recursive && isDirectory)
            {
                Enumerate(context, entry, recursive, maxResults, results);
            }
        }
    }

    private static string ToRelativePath(ToolExecutionContext context, string path)
    {
        return Path.GetRelativePath(context.Workspace.RootPath, path)
            .Replace(Path.DirectorySeparatorChar, '/');
    }
}
