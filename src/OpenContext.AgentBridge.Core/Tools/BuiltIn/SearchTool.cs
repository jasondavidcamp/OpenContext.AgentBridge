using OpenContext.AgentBridge.Core.Execution;

namespace OpenContext.AgentBridge.Core.Tools.BuiltIn;

public sealed class SearchTool : IAgentTool
{
    public ToolDefinition Definition { get; } = new(
        "search",
        "Search workspace text with ripgrep.",
        """{"query":"text or regex","path":"optional relative path","max_results":200}""");

    public async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        AgentDirective directive,
        CancellationToken cancellationToken = default)
    {
        var query = ToolArguments.GetRequiredString(directive.Arguments, "query");
        var path = ToolArguments.GetString(directive.Arguments, "path") ?? ".";
        var maxResults = ToolArguments.GetInt(directive.Arguments, "max_results", 200, 1, 1_000);
        var resolved = context.Workspace.ResolvePath(path);
        var relativePath = Path.GetRelativePath(context.Workspace.RootPath, resolved);
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            relativePath = ".";
        }

        relativePath = relativePath.Replace(Path.DirectorySeparatorChar, '/');

        var result = await context.Executor.RunAsync(
            context.Workspace,
            CommandRequest.Create(
                "rg",
                new[]
                {
                    "--line-number",
                    "--no-heading",
                    "--color",
                    "never",
                    "--glob",
                    "!**/.agentbridge/**",
                    "--glob",
                    "!**/bin/**",
                    "--glob",
                    "!**/obj/**",
                    query,
                    relativePath
                },
                TimeSpan.FromSeconds(30)),
            cancellationToken);

        if (result.ExitCode == 1)
        {
            return ToolResult.Success("No matches.");
        }

        if (result.ExitCode != 0)
        {
            return ToolResult.Failure(ToolText.Truncate(result.StandardError));
        }

        var allLines = result.StandardOutput
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .ToArray();
        var lines = allLines
            .Take(maxResults)
            .ToArray();
        var suffix = allLines.Length > maxResults
            ? $"{Environment.NewLine}[truncated at {maxResults} results]"
            : string.Empty;

        return ToolResult.Success(string.Join(Environment.NewLine, lines) + suffix);
    }
}
