using OpenContext.AgentBridge.Core.Execution;

namespace OpenContext.AgentBridge.Core.Tools.BuiltIn;

public sealed class ApplyPatchTool : IAgentTool
{
    public ToolDefinition Definition { get; } = new(
        "apply_patch",
        "Apply a unified diff patch inside the workspace after validating patch paths.",
        """{"patch":"unified diff text","check_only":false}""");

    public async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        AgentDirective directive,
        CancellationToken cancellationToken = default)
    {
        var patch = ToolArguments.GetRequiredString(directive.Arguments, "patch");
        if (!patch.EndsWith('\n'))
        {
            patch += Environment.NewLine;
        }

        patch = PatchPathNormalizer.Normalize(patch, context.Workspace.RootPath);
        var checkOnly = ToolArguments.GetBool(directive.Arguments, "check_only", false);
        var validation = PatchPathValidator.Validate(patch);

        if (!validation.IsSuccess)
        {
            return validation;
        }

        var patchDirectory = Path.Combine(context.Workspace.LocalStatePath, "patches");
        Directory.CreateDirectory(patchDirectory);

        var patchPath = Path.Combine(patchDirectory, $"{Guid.NewGuid():N}.patch");
        await File.WriteAllTextAsync(patchPath, patch, cancellationToken).ConfigureAwait(false);

        var relativePatchPath = ToToolPath(Path.GetRelativePath(context.Workspace.RootPath, patchPath));
        var checkResult = await RunGitApplyAsync(context, relativePatchPath, check: true, cancellationToken)
            .ConfigureAwait(false);

        if (!checkResult.IsSuccess || checkOnly)
        {
            return checkOnly && checkResult.IsSuccess
                ? ToolResult.Success($"Patch check passed for {validation.Content}.")
                : checkResult;
        }

        var applyResult = await RunGitApplyAsync(context, relativePatchPath, check: false, cancellationToken)
            .ConfigureAwait(false);

        return applyResult.IsSuccess
            ? ToolResult.Success($"Patch applied to {validation.Content}.")
            : applyResult;
    }

    private static async Task<ToolResult> RunGitApplyAsync(
        ToolExecutionContext context,
        string patchPath,
        bool check,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string> { "apply", "--whitespace=nowarn" };
        if (check)
        {
            arguments.Add("--check");
        }

        arguments.Add(patchPath);

        var result = await context.Executor.RunAsync(
            context.Workspace,
            CommandRequest.Create("git", arguments, TimeSpan.FromSeconds(30)),
            cancellationToken);

        var content = $"""
            Exit code: {result.ExitCode}

            STDOUT:
            {ToolText.Truncate(result.StandardOutput)}

            STDERR:
            {ToolText.Truncate(result.StandardError)}
            """;

        return result.ExitCode == 0
            ? ToolResult.Success(content)
            : ToolResult.Failure(content + Environment.NewLine + BuildPatchFailureHint());
    }

    private static string ToToolPath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string BuildPatchFailureHint()
    {
        return """
            Hint: Patch paths are interpreted relative to the active workspace. Re-read the target file, use exact current context lines, and retry with paths relative to the workspace root.
            """;
    }
}
