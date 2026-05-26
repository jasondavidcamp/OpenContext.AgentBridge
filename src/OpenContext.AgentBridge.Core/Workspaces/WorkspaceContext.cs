namespace OpenContext.AgentBridge.Core.Workspaces;

public sealed class WorkspaceContext
{
    private WorkspaceContext(string rootPath)
    {
        RootPath = rootPath;
        LocalStatePath = Path.Combine(RootPath, AgentBridgeDefaults.LocalStateDirectoryName);
        SkillsPath = Path.Combine(LocalStatePath, AgentBridgeDefaults.SkillsDirectoryName);
        ConversationDatabasePath = Path.Combine(LocalStatePath, AgentBridgeDefaults.ConversationDatabaseFileName);
        ConfigPath = Path.Combine(LocalStatePath, AgentBridgeDefaults.ConfigFileName);
        LogsPath = Path.Combine(LocalStatePath, AgentBridgeDefaults.LogsDirectoryName);
    }

    public string RootPath { get; }

    public string LocalStatePath { get; }

    public string SkillsPath { get; }

    public string ConversationDatabasePath { get; }

    public string ConfigPath { get; }

    public string LogsPath { get; }

    public static WorkspaceContext FromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A workspace path is required.", nameof(path));
        }

        var rootPath = Path.GetFullPath(path);

        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"Workspace does not exist: {rootPath}");
        }

        return new WorkspaceContext(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    public void EnsureLocalState()
    {
        Directory.CreateDirectory(LocalStatePath);
        Directory.CreateDirectory(SkillsPath);
        Directory.CreateDirectory(LogsPath);
    }

    public string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A path is required.", nameof(path));
        }

        var combined = Path.IsPathRooted(path)
            ? path
            : Path.Combine(RootPath, path);
        var fullPath = Path.GetFullPath(combined);

        if (!Contains(fullPath))
        {
            throw new InvalidOperationException($"Path escapes the workspace boundary: {path}");
        }

        return fullPath;
    }

    public bool Contains(string path)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.Equals(fullPath, RootPath, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(RootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(RootPath + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
