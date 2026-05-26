namespace OpenContext.AgentBridge.Core.Tools;

public sealed class ToolRegistry
{
    private readonly Dictionary<string, IAgentTool> _tools;

    public ToolRegistry(IEnumerable<IAgentTool> tools)
    {
        _tools = tools.ToDictionary(
            tool => tool.Definition.Name,
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ToolDefinition> Definitions => _tools.Values
        .Select(tool => tool.Definition)
        .OrderBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public bool TryGet(string name, out IAgentTool tool)
    {
        return _tools.TryGetValue(name, out tool!);
    }
}
