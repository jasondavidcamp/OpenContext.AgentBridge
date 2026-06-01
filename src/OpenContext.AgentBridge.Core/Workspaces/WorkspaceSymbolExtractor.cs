using System.Text.RegularExpressions;

namespace OpenContext.AgentBridge.Core.Workspaces;

public static partial class WorkspaceSymbolExtractor
{
    private const int MaxFileBytes = 256 * 1024;
    private const int MaxSymbolsPerFile = 12;

    private static readonly HashSet<string> CSharpTypeKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "class",
        "enum",
        "interface",
        "record",
        "struct"
    };

    public static IReadOnlyList<string> Extract(string rootPath, IEnumerable<string> relativePaths)
    {
        return relativePaths
            .SelectMany(path => Extract(rootPath, path))
            .ToArray();
    }

    public static IReadOnlyList<string> Extract(string rootPath, string relativePath)
    {
        return Path.GetExtension(relativePath).ToLowerInvariant() switch
        {
            ".cs" => ExtractCSharp(rootPath, relativePath),
            ".ps1" or ".psm1" => ExtractPowerShell(rootPath, relativePath),
            _ => Array.Empty<string>()
        };
    }

    private static IReadOnlyList<string> ExtractCSharp(string rootPath, string relativePath)
    {
        var content = ReadBoundedFile(rootPath, relativePath);
        if (content is null)
        {
            return Array.Empty<string>();
        }

        var symbols = new List<string>();
        var typeNameByDepth = new Dictionary<int, string>();
        var depth = 0;

        foreach (var rawLine in content.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
            {
                depth += CountChar(rawLine, '{') - CountChar(rawLine, '}');
                PruneTypeStack(typeNameByDepth, depth);
                continue;
            }

            var typeMatch = CSharpTypeRegex().Match(line);
            if (typeMatch.Success)
            {
                var kind = typeMatch.Groups["kind"].Value;
                var name = typeMatch.Groups["name"].Value;
                if (CSharpTypeKinds.Contains(kind))
                {
                    typeNameByDepth[depth] = name;
                    symbols.Add($"{relativePath}: {kind} {name}");
                    if (symbols.Count >= MaxSymbolsPerFile)
                    {
                        return symbols;
                    }
                }
            }

            var methodMatch = CSharpMethodRegex().Match(line);
            if (methodMatch.Success && !LooksLikeControlFlow(methodMatch.Groups["name"].Value))
            {
                var typeName = FindNearestTypeName(typeNameByDepth, depth);
                var methodName = methodMatch.Groups["name"].Value;
                var parameters = NormalizeWhitespace(methodMatch.Groups["parameters"].Value);
                var returnType = NormalizeWhitespace(methodMatch.Groups["returnType"].Value);
                var owner = typeName is null ? string.Empty : $"{typeName}.";
                symbols.Add($"{relativePath}: {returnType} {owner}{methodName}({parameters})");
                if (symbols.Count >= MaxSymbolsPerFile)
                {
                    return symbols;
                }
            }

            depth += CountChar(rawLine, '{') - CountChar(rawLine, '}');
            PruneTypeStack(typeNameByDepth, depth);
        }

        return symbols;
    }

    private static IReadOnlyList<string> ExtractPowerShell(string rootPath, string relativePath)
    {
        var content = ReadBoundedFile(rootPath, relativePath);
        if (content is null)
        {
            return Array.Empty<string>();
        }

        return PowerShellFunctionRegex()
            .Matches(content)
            .Select(match => $"{relativePath}: function {match.Groups["name"].Value}")
            .Take(MaxSymbolsPerFile)
            .ToArray();
    }

    private static string? ReadBoundedFile(string rootPath, string relativePath)
    {
        var normalizedRoot = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var path = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsInsideRoot(normalizedRoot, path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var info = new FileInfo(path);
            if (info.Length > MaxFileBytes)
            {
                return null;
            }

            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static bool IsInsideRoot(string rootPath, string path)
    {
        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.Equals(normalizedPath, rootPath, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(rootPath + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindNearestTypeName(Dictionary<int, string> typeNameByDepth, int depth)
    {
        for (var candidateDepth = depth; candidateDepth >= 0; candidateDepth--)
        {
            if (typeNameByDepth.TryGetValue(candidateDepth, out var name))
            {
                return name;
            }
        }

        return null;
    }

    private static void PruneTypeStack(Dictionary<int, string> typeNameByDepth, int depth)
    {
        foreach (var key in typeNameByDepth.Keys.Where(key => key > depth).ToArray())
        {
            typeNameByDepth.Remove(key);
        }
    }

    private static bool LooksLikeControlFlow(string name)
    {
        return name is "catch" or "for" or "foreach" or "if" or "lock" or "switch" or "using" or "while";
    }

    private static int CountChar(string value, char expected)
    {
        return value.Count(character => character == expected);
    }

    private static string NormalizeWhitespace(string value)
    {
        return WhitespaceRegex().Replace(value.Trim(), " ");
    }

    [GeneratedRegex("""^\s*(?:(?:public|private|protected|internal|static|sealed|abstract|partial|readonly|unsafe|async)\s+)*\b(?<kind>class|interface|record|struct|enum)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)""")]
    private static partial Regex CSharpTypeRegex();

    [GeneratedRegex("""^\s*(?:(?:public|private|protected|internal|static|virtual|override|abstract|sealed|async|extern|unsafe)\s+)+(?<returnType>[A-Za-z_][A-Za-z0-9_<>,\[\]\.?\s]*)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\((?<parameters>[^)]*)\)""")]
    private static partial Regex CSharpMethodRegex();

    [GeneratedRegex("""(?im)^\s*function\s+(?:global:|script:|local:|private:)?(?<name>[A-Za-z_][A-Za-z0-9_-]*)\b""")]
    private static partial Regex PowerShellFunctionRegex();

    [GeneratedRegex("""\s+""")]
    private static partial Regex WhitespaceRegex();
}
