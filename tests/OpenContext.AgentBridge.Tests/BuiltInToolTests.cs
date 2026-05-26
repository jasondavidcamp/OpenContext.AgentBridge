using System.Text.Json.Nodes;
using OpenContext.AgentBridge.Core.Execution;
using OpenContext.AgentBridge.Core.Tools;
using OpenContext.AgentBridge.Core.Tools.BuiltIn;
using OpenContext.AgentBridge.Core.Workspaces;

namespace OpenContext.AgentBridge.Tests;

public sealed class BuiltInToolTests
{
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
