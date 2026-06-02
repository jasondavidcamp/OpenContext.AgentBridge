using System.Diagnostics;
using OpenContext.AgentBridge.Core.Workspaces;

namespace OpenContext.AgentBridge.Tests;

public sealed class WorkspaceMapBuilderTests
{
    [Fact]
    public async Task Build_detects_repo_shape_and_ignores_local_state()
    {
        var root = CreateTempDirectory();

        try
        {
            await WriteAsync(root, "README.md", "# Sample");
            await WriteAsync(root, "Sample.sln", string.Empty);
            await WriteAsync(root, "global.json", "{}");
            await WriteAsync(root, "src/App/App.csproj", "<Project />");
            await WriteAsync(
                root,
                "src/App/Program.cs",
                """
                namespace Sample;

                public interface IGreetingService
                {
                    string Greet(string name);
                }

                public sealed class GreetingService : IGreetingService
                {
                    public string Greet(string name) => $"Hello, {name}!";
                }
                """);
            await WriteAsync(
                root,
                "scripts/Build.ps1",
                """
                function Invoke-SampleBuild {
                    Write-Host build
                }
                """);
            await WriteAsync(root, "skills/powershell.md", "# PowerShell");
            await WriteAsync(root, "docs/usage.md", "# Usage");
            await WriteAsync(root, ".agentbridge/config.json", "{}");
            await WriteAsync(root, "src/App/bin/Debug/generated.dll", string.Empty);

            var workspace = WorkspaceContext.FromPath(root);
            var map = WorkspaceMapBuilder.Build(workspace);

            Assert.Contains("src", map.TopLevelDirectories);
            Assert.Contains("scripts", map.TopLevelDirectories);
            Assert.Contains("README.md", map.RootFiles);
            Assert.Contains("Sample.sln", map.SolutionFiles);
            Assert.Contains("src/App/App.csproj", map.ProjectFiles);
            Assert.Contains("global.json", map.PackageFiles);
            Assert.Contains("src/App/Program.cs", map.SourceEntryPoints);
            Assert.Contains("scripts/Build.ps1", map.Scripts);
            Assert.Contains("src/App/Program.cs: interface IGreetingService", map.CodeSymbols);
            Assert.Contains("src/App/Program.cs: class GreetingService", map.CodeSymbols);
            Assert.Contains("src/App/Program.cs: string GreetingService.Greet(string name)", map.CodeSymbols);
            Assert.Contains("scripts/Build.ps1: function Invoke-SampleBuild", map.CodeSymbols);
            Assert.Contains("skills/powershell.md", map.Skills);
            Assert.Contains("docs/usage.md", map.Documentation);
            Assert.DoesNotContain(map.RootFiles, path => path.Contains(".agentbridge", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(map.ProjectFiles, path => path.Contains("/bin/", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Build_excludes_agentbridge_local_state_from_git_status()
    {
        var root = CreateTempDirectory();

        try
        {
            RunGit(root, "init");
            await WriteAsync(root, "README.md", "# Sample");
            await WriteAsync(root, ".agentbridge/agentbridge.db", "local state");

            var map = WorkspaceMapBuilder.Build(WorkspaceContext.FromPath(root));

            Assert.Contains("README.md", map.GitStatus);
            Assert.DoesNotContain(".agentbridge", map.GitStatus, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ToPromptText_formats_compact_workspace_orientation()
    {
        var root = CreateTempDirectory();

        try
        {
            await WriteAsync(root, "README.md", "# Sample");
            await WriteAsync(root, "src/App/App.csproj", "<Project />");

            var map = WorkspaceMapBuilder.Build(WorkspaceContext.FromPath(root));
            var text = map.ToPromptText();

            Assert.Contains("Root name:", text);
            Assert.Contains("Git status:", text);
            Assert.Contains("Projects: src/App/App.csproj", text);
            Assert.Contains("Code symbols:", text);
            Assert.Contains("Documentation: README.md", text);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"agentbridge-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task WriteAsync(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
    }

    private static void RunGit(string workingDirectory, params string[] arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }
}
