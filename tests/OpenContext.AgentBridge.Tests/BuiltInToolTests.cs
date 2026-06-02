using System.Diagnostics;
using System.Text.Json.Nodes;
using OpenContext.AgentBridge.Core.Execution;
using OpenContext.AgentBridge.Core.Tools;
using OpenContext.AgentBridge.Core.Tools.BuiltIn;
using OpenContext.AgentBridge.Core.Workspaces;

namespace OpenContext.AgentBridge.Tests;

public sealed class BuiltInToolTests
{
    [Fact]
    public async Task GitStatusTool_excludes_agentbridge_local_state()
    {
        var root = CreateTempDirectory();

        try
        {
            RunGit(root, "init");
            await File.WriteAllTextAsync(Path.Combine(root, "README.md"), "# Sample");
            Directory.CreateDirectory(Path.Combine(root, ".agentbridge"));
            await File.WriteAllTextAsync(Path.Combine(root, ".agentbridge", "agentbridge.db"), "local state");

            var workspace = WorkspaceContext.FromPath(root);
            var context = new ToolExecutionContext(workspace, new HostWorkspaceExecutor());
            var directive = new AgentDirective(
                "tool",
                "git_status",
                new JsonObject(),
                null);

            var result = await new GitStatusTool().ExecuteAsync(context, directive);

            Assert.True(result.IsSuccess, result.Content);
            Assert.Contains("README.md", result.Content);
            Assert.DoesNotContain(".agentbridge", result.Content, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyPatchTool_applies_unified_diff_inside_workspace()
    {
        var root = CreateTempDirectory();

        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "README.md"), $"one{Environment.NewLine}");

            var workspace = WorkspaceContext.FromPath(root);
            workspace.EnsureLocalState();
            var context = new ToolExecutionContext(workspace, new HostWorkspaceExecutor());
            var directive = new AgentDirective(
                "tool",
                "apply_patch",
                new JsonObject
                {
                    ["patch"] = """
                        diff --git a/README.md b/README.md
                        --- a/README.md
                        +++ b/README.md
                        @@ -1 +1 @@
                        -one
                        +two
                        """
                },
                null);

            var result = await new ApplyPatchTool().ExecuteAsync(context, directive);

            Assert.True(result.IsSuccess, result.Content);
            Assert.Equal($"two{Environment.NewLine}", await File.ReadAllTextAsync(Path.Combine(root, "README.md")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyPatchTool_check_only_does_not_modify_file()
    {
        var root = CreateTempDirectory();

        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "README.md"), $"one{Environment.NewLine}");

            var workspace = WorkspaceContext.FromPath(root);
            workspace.EnsureLocalState();
            var context = new ToolExecutionContext(workspace, new HostWorkspaceExecutor());
            var directive = new AgentDirective(
                "tool",
                "apply_patch",
                new JsonObject
                {
                    ["patch"] = """
                        diff --git a/README.md b/README.md
                        --- a/README.md
                        +++ b/README.md
                        @@ -1 +1 @@
                        -one
                        +two
                        """,
                    ["check_only"] = true
                },
                null);

            var result = await new ApplyPatchTool().ExecuteAsync(context, directive);

            Assert.True(result.IsSuccess, result.Content);
            Assert.Equal($"one{Environment.NewLine}", await File.ReadAllTextAsync(Path.Combine(root, "README.md")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyPatchTool_normalizes_workspace_prefixed_paths()
    {
        var root = CreateTempDirectory();

        try
        {
            var workspaceRoot = Path.Combine(root, "examples", "sandbox-project");
            var sourceDirectory = Path.Combine(workspaceRoot, "SandboxApp");
            Directory.CreateDirectory(sourceDirectory);
            var sourcePath = Path.Combine(sourceDirectory, "Program.cs");
            await File.WriteAllTextAsync(sourcePath, $"one{Environment.NewLine}");

            var workspace = WorkspaceContext.FromPath(workspaceRoot);
            workspace.EnsureLocalState();
            var context = new ToolExecutionContext(workspace, new HostWorkspaceExecutor());
            var directive = new AgentDirective(
                "tool",
                "apply_patch",
                new JsonObject
                {
                    ["patch"] = """
                        diff --git a/examples/sandbox-project/SandboxApp/Program.cs b/examples/sandbox-project/SandboxApp/Program.cs
                        --- a/examples/sandbox-project/SandboxApp/Program.cs
                        +++ b/examples/sandbox-project/SandboxApp/Program.cs
                        @@ -1 +1 @@
                        -one
                        +two
                        """
                },
                null);

            var result = await new ApplyPatchTool().ExecuteAsync(context, directive);

            Assert.True(result.IsSuccess, result.Content);
            Assert.Equal($"two{Environment.NewLine}", await File.ReadAllTextAsync(sourcePath));
            Assert.Contains("SandboxApp/Program.cs", result.Content);
            Assert.DoesNotContain("examples/sandbox-project/SandboxApp/Program.cs", result.Content);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyPatchTool_preserves_existing_workspace_relative_paths()
    {
        var root = CreateTempDirectory();

        try
        {
            var workspaceRoot = Path.Combine(root, "src");
            var nestedDirectory = Path.Combine(workspaceRoot, "src");
            Directory.CreateDirectory(nestedDirectory);
            var sourcePath = Path.Combine(nestedDirectory, "Script.ps1");
            await File.WriteAllTextAsync(sourcePath, $"one{Environment.NewLine}");

            var workspace = WorkspaceContext.FromPath(workspaceRoot);
            workspace.EnsureLocalState();
            var context = new ToolExecutionContext(workspace, new HostWorkspaceExecutor());
            var directive = new AgentDirective(
                "tool",
                "apply_patch",
                new JsonObject
                {
                    ["patch"] = """
                        diff --git a/src/Script.ps1 b/src/Script.ps1
                        --- a/src/Script.ps1
                        +++ b/src/Script.ps1
                        @@ -1 +1 @@
                        -one
                        +two
                        """
                },
                null);

            var result = await new ApplyPatchTool().ExecuteAsync(context, directive);

            Assert.True(result.IsSuccess, result.Content);
            Assert.Equal($"two{Environment.NewLine}", await File.ReadAllTextAsync(sourcePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyPatchTool_rejects_parent_traversal_paths()
    {
        var root = CreateTempDirectory();

        try
        {
            var workspace = WorkspaceContext.FromPath(root);
            workspace.EnsureLocalState();
            var context = new ToolExecutionContext(workspace, new HostWorkspaceExecutor());
            var directive = new AgentDirective(
                "tool",
                "apply_patch",
                new JsonObject
                {
                    ["patch"] = """
                        diff --git a/../outside.txt b/../outside.txt
                        --- a/../outside.txt
                        +++ b/../outside.txt
                        @@ -1 +1 @@
                        -one
                        +two
                        """
                },
                null);

            var result = await new ApplyPatchTool().ExecuteAsync(context, directive);

            Assert.False(result.IsSuccess);
            Assert.Contains("escapes the workspace", result.Content);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyPatchTool_failure_includes_retry_guidance()
    {
        var root = CreateTempDirectory();

        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "README.md"), $"one{Environment.NewLine}");

            var workspace = WorkspaceContext.FromPath(root);
            workspace.EnsureLocalState();
            var context = new ToolExecutionContext(workspace, new HostWorkspaceExecutor());
            var directive = new AgentDirective(
                "tool",
                "apply_patch",
                new JsonObject
                {
                    ["patch"] = """
                        diff --git a/README.md b/README.md
                        --- a/README.md
                        +++ b/README.md
                        @@ -1 +1 @@
                        -missing
                        +two
                        """
                },
                null);

            var result = await new ApplyPatchTool().ExecuteAsync(context, directive);

            Assert.False(result.IsSuccess);
            Assert.Contains("retry with paths relative to the workspace root", result.Content);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WriteFileTool_writes_inside_workspace()
    {
        var root = CreateTempDirectory();

        try
        {
            var workspace = WorkspaceContext.FromPath(root);
            var context = new ToolExecutionContext(workspace, new HostWorkspaceExecutor());
            var directive = new AgentDirective(
                "tool",
                "write_file",
                new JsonObject
                {
                    ["path"] = "src/example.txt",
                    ["content"] = "hello"
                },
                null);

            var result = await new WriteFileTool().ExecuteAsync(context, directive);

            Assert.True(result.IsSuccess);
            Assert.Equal("hello", await File.ReadAllTextAsync(Path.Combine(root, "src", "example.txt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReplaceTextTool_replaces_single_exact_occurrence()
    {
        var root = CreateTempDirectory();

        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "script.ps1"), "Hello, AgentLab.");

            var workspace = WorkspaceContext.FromPath(root);
            var context = new ToolExecutionContext(workspace, new HostWorkspaceExecutor());
            var directive = new AgentDirective(
                "tool",
                "replace_text",
                new JsonObject
                {
                    ["path"] = "script.ps1",
                    ["old_text"] = "Hello, AgentLab.",
                    ["new_text"] = "Hello, AgentLab from AgentBridge."
                },
                null);

            var result = await new ReplaceTextTool().ExecuteAsync(context, directive);

            Assert.True(result.IsSuccess, result.Content);
            Assert.Equal("Hello, AgentLab from AgentBridge.", await File.ReadAllTextAsync(Path.Combine(root, "script.ps1")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReplaceTextTool_rejects_multiple_occurrences_without_replace_all()
    {
        var root = CreateTempDirectory();

        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "script.ps1"), "Hello. Hello.");

            var workspace = WorkspaceContext.FromPath(root);
            var context = new ToolExecutionContext(workspace, new HostWorkspaceExecutor());
            var directive = new AgentDirective(
                "tool",
                "replace_text",
                new JsonObject
                {
                    ["path"] = "script.ps1",
                    ["old_text"] = "Hello.",
                    ["new_text"] = "Hi."
                },
                null);

            var result = await new ReplaceTextTool().ExecuteAsync(context, directive);

            Assert.False(result.IsSuccess);
            Assert.Contains("replace_all=true", result.Content);
            Assert.Equal("Hello. Hello.", await File.ReadAllTextAsync(Path.Combine(root, "script.ps1")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReplaceTextTool_replaces_all_when_requested()
    {
        var root = CreateTempDirectory();

        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "script.ps1"), "Hello. Hello.");

            var workspace = WorkspaceContext.FromPath(root);
            var context = new ToolExecutionContext(workspace, new HostWorkspaceExecutor());
            var directive = new AgentDirective(
                "tool",
                "replace_text",
                new JsonObject
                {
                    ["path"] = "script.ps1",
                    ["old_text"] = "Hello.",
                    ["new_text"] = "Hi.",
                    ["replace_all"] = true,
                    ["expected_replacements"] = 2
                },
                null);

            var result = await new ReplaceTextTool().ExecuteAsync(context, directive);

            Assert.True(result.IsSuccess, result.Content);
            Assert.Equal("Hi. Hi.", await File.ReadAllTextAsync(Path.Combine(root, "script.ps1")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WriteFileTool_rejects_paths_outside_workspace()
    {
        var root = CreateTempDirectory();

        try
        {
            var workspace = WorkspaceContext.FromPath(root);
            var context = new ToolExecutionContext(workspace, new HostWorkspaceExecutor());
            var directive = new AgentDirective(
                "tool",
                "write_file",
                new JsonObject
                {
                    ["path"] = "../outside.txt",
                    ["content"] = "nope"
                },
                null);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => new WriteFileTool().ExecuteAsync(context, directive));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WriteFileTool_allows_empty_file_content()
    {
        var root = CreateTempDirectory();

        try
        {
            var workspace = WorkspaceContext.FromPath(root);
            var context = new ToolExecutionContext(workspace, new HostWorkspaceExecutor());
            var directive = new AgentDirective(
                "tool",
                "write_file",
                new JsonObject
                {
                    ["path"] = "empty.txt",
                    ["content"] = string.Empty
                },
                null);

            var result = await new WriteFileTool().ExecuteAsync(context, directive);

            Assert.True(result.IsSuccess);
            Assert.Equal(string.Empty, await File.ReadAllTextAsync(Path.Combine(root, "empty.txt")));
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
