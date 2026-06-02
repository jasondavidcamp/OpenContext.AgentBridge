using System.Text.Json;
using OpenContext.AgentBridge.Core.Conversation;

namespace OpenContext.AgentBridge.Core.Tools;

public static class ToolChangedFileExtractor
{
    public static IReadOnlyList<string> Extract(IReadOnlyList<ToolCallRecord> toolCalls)
    {
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var toolCall in toolCalls.Where(toolCall => toolCall.IsSuccess))
        {
            foreach (var path in Extract(toolCall))
            {
                if (seen.Add(path))
                {
                    paths.Add(path);
                }
            }
        }

        return paths;
    }

    private static IEnumerable<string> Extract(ToolCallRecord toolCall)
    {
        return toolCall.ToolName.ToLowerInvariant() switch
        {
            "replace_text" or "write_file" => ExtractPathArgument(toolCall.ArgumentsJson),
            "apply_patch" => ExtractPatchPaths(toolCall.ArgumentsJson),
            _ => Array.Empty<string>()
        };
    }

    private static IEnumerable<string> ExtractPathArgument(string argumentsJson)
    {
        var path = TryGetString(argumentsJson, "path");
        if (NormalizeSafeRelativePath(path) is { } normalized)
        {
            yield return normalized;
        }
    }

    private static IEnumerable<string> ExtractPatchPaths(string argumentsJson)
    {
        if (TryGetBool(argumentsJson, "check_only"))
        {
            yield break;
        }

        var patch = TryGetString(argumentsJson, "patch");
        if (string.IsNullOrWhiteSpace(patch))
        {
            yield break;
        }

        using var reader = new StringReader(patch);
        while (reader.ReadLine() is { } line)
        {
            if (!line.StartsWith("--- ", StringComparison.Ordinal)
                && !line.StartsWith("+++ ", StringComparison.Ordinal))
            {
                continue;
            }

            var token = line[4..];
            var tabIndex = token.IndexOf('\t');
            if (tabIndex >= 0)
            {
                token = token[..tabIndex];
            }

            if (NormalizeSafeRelativePath(token) is { } normalized)
            {
                yield return normalized;
            }
        }
    }

    private static string? TryGetString(string argumentsJson, string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(argumentsJson);
            return document.RootElement.TryGetProperty(propertyName, out var property)
                   && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryGetBool(string argumentsJson, string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(argumentsJson);
            return document.RootElement.TryGetProperty(propertyName, out var property)
                   && property.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? NormalizeSafeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        path = path.Trim().Trim('"').Replace('\\', '/');
        if (string.Equals(path, "/dev/null", StringComparison.Ordinal))
        {
            return null;
        }

        if (path.StartsWith("a/", StringComparison.Ordinal)
            || path.StartsWith("b/", StringComparison.Ordinal))
        {
            path = path[2..];
        }

        if (string.IsNullOrWhiteSpace(path)
            || path.StartsWith("/", StringComparison.Ordinal)
            || Path.IsPathRooted(path))
        {
            return null;
        }

        return path
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .All(segment => segment is not "." and not "..")
            ? path
            : null;
    }
}
