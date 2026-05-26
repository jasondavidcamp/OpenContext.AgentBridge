using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenContext.AgentBridge.Core.Tools;

public static class AgentResponseParser
{
    public static bool TryParse(string content, out AgentDirective directive)
    {
        directive = new AgentDirective("final", null, new JsonObject(), content);

        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var payload = ExtractJsonPayload(content);
        if (payload is null)
        {
            return false;
        }

        try
        {
            var node = JsonNode.Parse(payload)?.AsObject();
            if (node is null)
            {
                return false;
            }

            var type = node["type"]?.GetValue<string>();
            if (string.Equals(type, "final", StringComparison.OrdinalIgnoreCase))
            {
                directive = new AgentDirective(
                    "final",
                    null,
                    new JsonObject(),
                    node["message"]?.GetValue<string>() ?? string.Empty);
                return true;
            }

            if (string.Equals(type, "tool", StringComparison.OrdinalIgnoreCase))
            {
                var toolName = node["tool"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(toolName))
                {
                    return false;
                }

                var arguments = node["arguments"]?.DeepClone() as JsonObject ?? new JsonObject();
                directive = new AgentDirective("tool", toolName, arguments, null);
                return true;
            }
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        return false;
    }

    private static string? ExtractJsonPayload(string content)
    {
        var trimmed = content.Trim();

        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = trimmed.IndexOf('\n');
            var closingFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);

            if (firstLineEnd >= 0 && closingFence > firstLineEnd)
            {
                return trimmed[(firstLineEnd + 1)..closingFence].Trim();
            }
        }

        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            return trimmed;
        }

        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');

        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            return trimmed[firstBrace..(lastBrace + 1)];
        }

        return null;
    }
}
