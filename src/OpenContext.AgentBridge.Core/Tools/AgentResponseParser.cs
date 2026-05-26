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

        foreach (var payload in ExtractJsonPayloads(content))
        {
            try
            {
                var node = JsonNode.Parse(payload)?.AsObject();
                if (node is null)
                {
                    continue;
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
                        continue;
                    }

                    var arguments = node["arguments"]?.DeepClone() as JsonObject ?? new JsonObject();
                    directive = new AgentDirective("tool", toolName, arguments, null);
                    return true;
                }
            }
            catch (JsonException)
            {
                continue;
            }
            catch (InvalidOperationException)
            {
                continue;
            }
        }

        return false;
    }

    private static IEnumerable<string> ExtractJsonPayloads(string content)
    {
        var trimmed = content.Trim();

        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = trimmed.IndexOf('\n');
            var closingFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);

            if (firstLineEnd >= 0 && closingFence > firstLineEnd)
            {
                yield return trimmed[(firstLineEnd + 1)..closingFence].Trim();
            }
        }

        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            yield return trimmed;
        }

        foreach (var payload in ExtractBalancedJsonObjects(trimmed))
        {
            yield return payload;
        }
    }

    private static IEnumerable<string> ExtractBalancedJsonObjects(string content)
    {
        for (var start = content.IndexOf('{'); start >= 0 && start < content.Length; start = content.IndexOf('{', start + 1))
        {
            var depth = 0;
            var inString = false;
            var escaped = false;

            for (var index = start; index < content.Length; index++)
            {
                var current = content[index];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (current == '\\')
                    {
                        escaped = true;
                    }
                    else if (current == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (current == '"')
                {
                    inString = true;
                    continue;
                }

                if (current == '{')
                {
                    depth++;
                }
                else if (current == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        yield return content[start..(index + 1)];
                        break;
                    }
                }
            }
        }
    }
}
