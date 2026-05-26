namespace OpenContext.AgentBridge.Core.Tools.BuiltIn;

internal static class PatchPathValidator
{
    public static ToolResult Validate(string patch)
    {
        var paths = ExtractPaths(patch).ToArray();

        if (paths.Length == 0)
        {
            return ToolResult.Failure("Patch does not contain any file paths.");
        }

        foreach (var path in paths)
        {
            if (!IsSafeRelativePath(path))
            {
                return ToolResult.Failure($"Patch path escapes the workspace boundary: {path}");
            }
        }

        return ToolResult.Success(string.Join(", ", paths.Distinct(StringComparer.Ordinal)));
    }

    private static IEnumerable<string> ExtractPaths(string patch)
    {
        using var reader = new StringReader(patch);

        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith("--- ", StringComparison.Ordinal)
                || line.StartsWith("+++ ", StringComparison.Ordinal))
            {
                var path = NormalizePathToken(line[4..]);
                if (path is not null)
                {
                    yield return path;
                }
            }
        }
    }

    private static string? NormalizePathToken(string token)
    {
        var path = token.Trim();
        var tabIndex = path.IndexOf('\t');
        if (tabIndex >= 0)
        {
            path = path[..tabIndex];
        }

        path = path.Trim().Trim('"');

        if (string.Equals(path, "/dev/null", StringComparison.Ordinal))
        {
            return null;
        }

        if (path.StartsWith("a/", StringComparison.Ordinal)
            || path.StartsWith("b/", StringComparison.Ordinal))
        {
            path = path[2..];
        }

        return path.Replace('\\', '/');
    }

    private static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || path.StartsWith("/", StringComparison.Ordinal)
            || Path.IsPathRooted(path))
        {
            return false;
        }

        return path
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .All(segment => segment is not "." and not "..");
    }
}
