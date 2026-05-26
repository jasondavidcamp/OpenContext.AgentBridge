using OpenContext.AgentBridge.Core.Workspaces;

namespace OpenContext.AgentBridge.Tests;

public sealed class WorkspaceContextTests
{
    [Fact]
    public void ResolvePath_allows_paths_inside_workspace()
    {
        var root = CreateTempDirectory();

        try
        {
            var workspace = WorkspaceContext.FromPath(root);
            var resolved = workspace.ResolvePath(Path.Combine("src", "Program.cs"));

            Assert.StartsWith(root, resolved, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolvePath_rejects_parent_traversal()
    {
        var root = CreateTempDirectory();

        try
        {
            var workspace = WorkspaceContext.FromPath(root);

            Assert.Throws<InvalidOperationException>(
                () => workspace.ResolvePath(Path.Combine("..", "outside.txt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Contains_does_not_match_sibling_with_same_prefix()
    {
        var parent = CreateTempDirectory();
        var root = Path.Combine(parent, "repo");
        var sibling = Path.Combine(parent, "repo-other", "file.txt");

        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.GetDirectoryName(sibling)!);

            var workspace = WorkspaceContext.FromPath(root);

            Assert.False(workspace.Contains(sibling));
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"agentbridge-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
