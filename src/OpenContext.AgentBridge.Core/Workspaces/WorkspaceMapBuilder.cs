using System.Diagnostics;

namespace OpenContext.AgentBridge.Core.Workspaces;

public static class WorkspaceMapBuilder
{
    private const int MaxFilesToInspect = 5_000;
    private const int MaxGitStatusLines = 12;
    private const int MaxGroupItems = 24;
    private const int MaxSymbolItems = 40;

    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".agentbridge",
        ".git",
        ".hg",
        ".svn",
        ".vs",
        ".vscode",
        ".idea",
        "bin",
        "dist",
        "node_modules",
        "obj",
        "out",
        "packages",
        "target",
        "TestResults"
    };

    private static readonly HashSet<string> ProjectExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csproj",
        ".fsproj",
        ".vbproj",
        ".sqlproj",
        ".esproj"
    };

    private static readonly HashSet<string> ScriptExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ps1",
        ".psd1",
        ".psm1",
        ".sh",
        ".bat",
        ".cmd"
    };

    private static readonly HashSet<string> DocumentationExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md",
        ".mdx",
        ".txt",
        ".rst"
    };

    private static readonly HashSet<string> PackageFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".editorconfig",
        ".gitattributes",
        ".gitignore",
        "Directory.Build.props",
        "Directory.Build.targets",
        "Directory.Packages.props",
        "global.json",
        "package-lock.json",
        "package.json",
        "pnpm-lock.yaml",
        "pyproject.toml",
        "requirements.txt",
        "tsconfig.json",
        "yarn.lock"
    };

    private static readonly HashSet<string> EntryPointFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "App.tsx",
        "index.cs",
        "index.ts",
        "index.tsx",
        "main.cs",
        "main.ts",
        "Program.cs",
        "server.ts",
        "Startup.cs"
    };

    public static WorkspaceMap Build(WorkspaceContext workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var files = EnumerateFiles(workspace.RootPath)
            .Select(path => ToRelativePath(workspace.RootPath, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var topLevelDirectories = EnumerateTopLevelDirectories(workspace.RootPath);

        return new WorkspaceMap(
            Path.GetFileName(workspace.RootPath),
            ReadGitStatus(workspace.RootPath),
            Limit(topLevelDirectories),
            Limit(files.Where(IsRootFile)),
            Limit(files.Where(IsSolutionFile)),
            Limit(files.Where(IsProjectFile)),
            Limit(files.Where(IsPackageFile)),
            Limit(files.Where(IsLikelyEntryPoint)),
            Limit(
                WorkspaceSymbolExtractor.Extract(
                    workspace.RootPath,
                    files.Where(IsSymbolSourceFile)),
                MaxSymbolItems),
            Limit(files.Where(IsScriptFile)),
            Limit(files.Where(IsSkillFile)),
            Limit(files.Where(IsDocumentationFile)));
    }

    private static IEnumerable<string> EnumerateFiles(string rootPath)
    {
        var stack = new Stack<string>();
        stack.Push(rootPath);
        var inspected = 0;

        while (stack.Count > 0 && inspected < MaxFilesToInspect)
        {
            var directory = stack.Pop();
            IEnumerable<string> childDirectories;
            IEnumerable<string> childFiles;

            try
            {
                childDirectories = Directory.EnumerateDirectories(directory);
                childFiles = Directory.EnumerateFiles(directory);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
            {
                continue;
            }

            foreach (var childDirectory in childDirectories.OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (!ShouldIgnoreDirectory(childDirectory))
                {
                    stack.Push(childDirectory);
                }
            }

            foreach (var file in childFiles)
            {
                inspected++;
                yield return file;

                if (inspected >= MaxFilesToInspect)
                {
                    yield break;
                }
            }
        }
    }

    private static IReadOnlyList<string> EnumerateTopLevelDirectories(string rootPath)
    {
        try
        {
            return Directory
                .EnumerateDirectories(rootPath)
                .Where(path => !ShouldIgnoreDirectory(path))
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
        {
            return Array.Empty<string>();
        }
    }

    private static bool ShouldIgnoreDirectory(string path)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (IgnoredDirectoryNames.Contains(name))
        {
            return true;
        }

        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
        {
            return true;
        }
    }

    private static string ReadGitStatus(string rootPath)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = rootPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            process.StartInfo.ArgumentList.Add("status");
            process.StartInfo.ArgumentList.Add("--short");
            process.StartInfo.ArgumentList.Add("--");
            process.StartInfo.ArgumentList.Add(".");
            process.StartInfo.ArgumentList.Add(":(exclude).agentbridge");
            process.Start();

            if (!process.WaitForExit(2_000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                return "unavailable: git status timed out";
            }

            var output = process.StandardOutput.ReadToEnd();
            if (process.ExitCode != 0)
            {
                return "unavailable: not a git repository or git could not run";
            }

            var lines = output
                .ReplaceLineEndings("\n")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Take(MaxGitStatusLines + 1)
                .ToArray();
            if (lines.Length == 0)
            {
                return "clean";
            }

            var visible = lines.Take(MaxGitStatusLines).ToArray();
            var suffix = lines.Length > MaxGitStatusLines ? "; ..." : string.Empty;

            return string.Join("; ", visible) + suffix;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return "unavailable: git could not run";
        }
    }

    private static string ToRelativePath(string rootPath, string path)
    {
        return Path.GetRelativePath(rootPath, path)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static bool IsRootFile(string path)
    {
        return !path.Contains('/');
    }

    private static bool IsSolutionFile(string path)
    {
        return string.Equals(Path.GetExtension(path), ".sln", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetExtension(path), ".slnx", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProjectFile(string path)
    {
        return ProjectExtensions.Contains(Path.GetExtension(path));
    }

    private static bool IsPackageFile(string path)
    {
        return PackageFileNames.Contains(Path.GetFileName(path));
    }

    private static bool IsLikelyEntryPoint(string path)
    {
        return EntryPointFileNames.Contains(Path.GetFileName(path));
    }

    private static bool IsScriptFile(string path)
    {
        return ScriptExtensions.Contains(Path.GetExtension(path));
    }

    private static bool IsSymbolSourceFile(string path)
    {
        return string.Equals(Path.GetExtension(path), ".cs", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetExtension(path), ".ps1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetExtension(path), ".psm1", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSkillFile(string path)
    {
        return path.StartsWith("skills/", StringComparison.OrdinalIgnoreCase)
            && DocumentationExtensions.Contains(Path.GetExtension(path));
    }

    private static bool IsDocumentationFile(string path)
    {
        var fileName = Path.GetFileName(path);
        return DocumentationExtensions.Contains(Path.GetExtension(path))
            && (path.StartsWith("docs/", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fileName, "README.md", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fileName, "CHANGELOG.md", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> Limit(IEnumerable<string> paths, int maxItems = MaxGroupItems)
    {
        return paths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(maxItems)
            .ToArray();
    }
}
