namespace OpenContext.AgentBridge.Core.Tools.BuiltIn;

internal static class PatchPathNormalizer
{
    public static string Normalize(string patch, string workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(patch);
        ArgumentNullException.ThrowIfNull(workspaceRoot);

        using var reader = new StringReader(patch);
        var writer = new StringWriter();
        var firstLine = true;

        while (reader.ReadLine() is { } line)
        {
            if (!firstLine)
            {
                writer.WriteLine();
            }

            writer.Write(RewriteLine(line, workspaceRoot));
            firstLine = false;
        }

        if (patch.EndsWith('\n'))
        {
            writer.WriteLine();
        }

        return writer.ToString();
    }

    private static string RewriteLine(string line, string workspaceRoot)
    {
        if (line.StartsWith("diff --git ", StringComparison.Ordinal))
        {
            return RewriteDiffGitLine(line, workspaceRoot);
        }

        if (line.StartsWith("--- ", StringComparison.Ordinal)
            || line.StartsWith("+++ ", StringComparison.Ordinal))
        {
            return line[..4] + RewritePathToken(line[4..], workspaceRoot);
        }

        var renameOrCopyPrefix = GetRenameOrCopyPrefix(line);
        return renameOrCopyPrefix is null
            ? line
            : renameOrCopyPrefix + RewritePathToken(line[renameOrCopyPrefix.Length..], workspaceRoot);
    }

    private static string RewriteDiffGitLine(string line, string workspaceRoot)
    {
        const string prefix = "diff --git ";
        var tokens = line[prefix.Length..]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length != 2)
        {
            return line;
        }

        return prefix
            + RewritePathToken(tokens[0], workspaceRoot)
            + " "
            + RewritePathToken(tokens[1], workspaceRoot);
    }

    private static string? GetRenameOrCopyPrefix(string line)
    {
        string[] prefixes =
        {
            "rename from ",
            "rename to ",
            "copy from ",
            "copy to "
        };

        return prefixes.FirstOrDefault(prefix => line.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static string RewritePathToken(string token, string workspaceRoot)
    {
        var suffix = string.Empty;
        var path = token.Trim();
        var tabIndex = path.IndexOf('\t');
        if (tabIndex >= 0)
        {
            suffix = path[tabIndex..];
            path = path[..tabIndex];
        }

        path = path.Trim().Trim('"');
        if (string.Equals(path, "/dev/null", StringComparison.Ordinal))
        {
            return token;
        }

        var diffPrefix = string.Empty;
        if (path.StartsWith("a/", StringComparison.Ordinal)
            || path.StartsWith("b/", StringComparison.Ordinal))
        {
            diffPrefix = path[..2];
            path = path[2..];
        }

        var normalized = NormalizeWorkspacePrefixedPath(path.Replace('\\', '/'), workspaceRoot);
        return diffPrefix + normalized + suffix;
    }

    private static string NormalizeWorkspacePrefixedPath(string path, string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(path)
            || path.StartsWith("/", StringComparison.Ordinal)
            || Path.IsPathRooted(path)
            || path.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment is "." or ".."))
        {
            return path;
        }

        if (HasExistingTargetOrNestedParent(workspaceRoot, path))
        {
            return path;
        }

        var pathSegments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var workspaceSegments = Path.GetFullPath(workspaceRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

        for (var prefixLength = Math.Min(pathSegments.Length - 1, workspaceSegments.Length);
             prefixLength > 0;
             prefixLength--)
        {
            var workspaceSuffix = workspaceSegments[^prefixLength..];
            if (!workspaceSuffix.SequenceEqual(pathSegments[..prefixLength], StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            return string.Join('/', pathSegments[prefixLength..]);
        }

        return path;
    }

    private static bool HasExistingTargetOrNestedParent(string workspaceRoot, string path)
    {
        var fullPath = Path.GetFullPath(Path.Combine(workspaceRoot, path));
        if (File.Exists(fullPath) || Directory.Exists(fullPath))
        {
            return true;
        }

        var workspace = Path.GetFullPath(workspaceRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Path.GetDirectoryName(fullPath);

        while (!string.IsNullOrWhiteSpace(parent))
        {
            var normalizedParent = parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(normalizedParent, workspace, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (Directory.Exists(normalizedParent))
            {
                return true;
            }

            parent = Path.GetDirectoryName(normalizedParent);
        }

        return false;
    }
}
