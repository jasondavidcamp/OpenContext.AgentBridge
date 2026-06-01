using System.Text;

namespace OpenContext.AgentBridge.Core.Workspaces;

public sealed record WorkspaceMap(
    string RootName,
    string GitStatus,
    IReadOnlyList<string> TopLevelDirectories,
    IReadOnlyList<string> RootFiles,
    IReadOnlyList<string> SolutionFiles,
    IReadOnlyList<string> ProjectFiles,
    IReadOnlyList<string> PackageFiles,
    IReadOnlyList<string> SourceEntryPoints,
    IReadOnlyList<string> CodeSymbols,
    IReadOnlyList<string> Scripts,
    IReadOnlyList<string> Skills,
    IReadOnlyList<string> Documentation)
{
    public string ToPromptText()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Root name: {RootName}");
        builder.AppendLine($"Git status: {GitStatus}");
        AppendGroup(builder, "Top-level directories", TopLevelDirectories);
        AppendGroup(builder, "Root files", RootFiles);
        AppendGroup(builder, "Solutions", SolutionFiles);
        AppendGroup(builder, "Projects", ProjectFiles);
        AppendGroup(builder, "Package/config files", PackageFiles);
        AppendGroup(builder, "Likely entry points", SourceEntryPoints);
        AppendGroup(builder, "Code symbols", CodeSymbols);
        AppendGroup(builder, "Scripts", Scripts);
        AppendGroup(builder, "Skills", Skills);
        AppendGroup(builder, "Documentation", Documentation);

        return builder.ToString().TrimEnd();
    }

    private static void AppendGroup(
        StringBuilder builder,
        string label,
        IReadOnlyList<string> paths)
    {
        builder.Append(label);
        builder.Append(": ");
        builder.AppendLine(paths.Count == 0 ? "none detected" : string.Join(", ", paths));
    }
}
