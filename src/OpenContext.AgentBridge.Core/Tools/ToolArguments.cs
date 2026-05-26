using System.Text.Json.Nodes;

namespace OpenContext.AgentBridge.Core.Tools;

internal static class ToolArguments
{
    public static string? GetString(JsonObject arguments, string name)
    {
        return arguments.TryGetPropertyValue(name, out var node)
            ? node?.GetValue<string>()
            : null;
    }

    public static string GetRequiredString(JsonObject arguments, string name)
    {
        var value = GetString(arguments, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Missing required argument: {name}");
        }

        return value;
    }

    public static int GetInt(JsonObject arguments, string name, int defaultValue, int minValue, int maxValue)
    {
        if (!arguments.TryGetPropertyValue(name, out var node) || node is null)
        {
            return defaultValue;
        }

        var value = node.GetValue<int>();
        return Math.Clamp(value, minValue, maxValue);
    }

    public static bool GetBool(JsonObject arguments, string name, bool defaultValue)
    {
        if (!arguments.TryGetPropertyValue(name, out var node) || node is null)
        {
            return defaultValue;
        }

        return node.GetValue<bool>();
    }
}
