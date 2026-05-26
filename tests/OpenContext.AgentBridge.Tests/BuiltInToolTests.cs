using System.Text.Json.Nodes;
using OpenContext.AgentBridge.Core.Execution;
using OpenContext.AgentBridge.Core.Tools;
using OpenContext.AgentBridge.Core.Tools.BuiltIn;
using OpenContext.AgentBridge.Core.Workspaces;

namespace OpenContext.AgentBridge.Tests;

public sealed class BuiltInToolTests
{
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
}
