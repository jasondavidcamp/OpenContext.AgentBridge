namespace OpenContext.AgentBridge.Core.Tools;

public sealed class ToolRegistry
{
    private static readonly IReadOnlyDictionary<string, string> ToolAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["bash"] = "run_command",
        ["cat"] = "read_file",
        ["dir"] = "list_files",
        ["execute_command"] = "run_command",
        ["grep"] = "search",
        ["list_dir"] = "list_files",
        ["list_directory"] = "list_files",
        ["ls"] = "list_files",
        ["replace_in_file"] = "replace_text",
        ["run_shell_command"] = "run_command",
        ["shell"] = "run_command",
        ["shell_command"] = "run_command",
        ["show_diff"] = "git_diff",
        ["status"] = "git_status",
        ["view_diff"] = "git_diff",
        ["view_file"] = "read_file"
    };

    private readonly Dictionary<string, RegisteredTool> _tools;

    public ToolRegistry(IEnumerable<IAgentTool> tools)
    {
        _tools = tools.ToDictionary(
            tool => tool.Definition.Name,
            tool => new RegisteredTool(tool, tool.Definition.Name, IsAlias: false),
            StringComparer.OrdinalIgnoreCase);

        foreach (var alias in ToolAliases)
        {
            if (_tools.ContainsKey(alias.Key) || !_tools.TryGetValue(alias.Value, out var canonicalTool))
            {
                continue;
            }

            _tools[alias.Key] = canonicalTool with { IsAlias = true };
        }
    }

    public IReadOnlyList<ToolDefinition> Definitions => _tools.Values
        .Where(tool => !tool.IsAlias)
        .Select(tool => tool.Tool.Definition)
        .OrderBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public bool TryGet(string name, out IAgentTool tool)
    {
        if (_tools.TryGetValue(name, out var registeredTool))
        {
            tool = registeredTool.Tool;
            return true;
        }

        tool = null!;
        return false;
    }

    public bool TryGetCanonicalName(string name, out string canonicalName)
    {
        if (_tools.TryGetValue(name, out var registeredTool))
        {
            canonicalName = registeredTool.CanonicalName;
            return true;
        }

        canonicalName = string.Empty;
        return false;
    }

    private sealed record RegisteredTool(
        IAgentTool Tool,
        string CanonicalName,
        bool IsAlias);
}
