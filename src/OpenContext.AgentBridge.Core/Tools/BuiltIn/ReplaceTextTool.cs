namespace OpenContext.AgentBridge.Core.Tools.BuiltIn;

public sealed class ReplaceTextTool : IAgentTool
{
    public ToolDefinition Definition { get; } = new(
        "replace_text",
        "Replace exact text in one workspace file. Safer than write_file for small substitutions when the old text can be matched exactly.",
        """{"path":"relative file path","old_text":"exact text to replace","new_text":"replacement text","replace_all":false,"expected_replacements":1}""");

    public async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        AgentDirective directive,
        CancellationToken cancellationToken = default)
    {
        var path = ToolArguments.GetRequiredString(directive.Arguments, "path");
        var oldText = ToolArguments.GetString(directive.Arguments, "old_text");
        if (string.IsNullOrEmpty(oldText))
        {
            return ToolResult.Failure("Missing required argument: old_text");
        }

        var newText = ToolArguments.GetString(directive.Arguments, "new_text");
        if (newText is null)
        {
            return ToolResult.Failure("Missing required argument: new_text");
        }

        var replaceAll = ToolArguments.GetBool(directive.Arguments, "replace_all", false);
        var expectedReplacements = ToolArguments.GetInt(directive.Arguments, "expected_replacements", -1, -1, 100_000);
        var resolved = context.Workspace.ResolvePath(path);

        if (!File.Exists(resolved))
        {
            return ToolResult.Failure($"File does not exist: {path}");
        }

        var content = await File.ReadAllTextAsync(resolved, cancellationToken).ConfigureAwait(false);
        var occurrences = CountOccurrences(content, oldText);
        if (occurrences == 0)
        {
            return ToolResult.Failure($"""
                Text was not found in {path}.
                Re-read the file and use exact current text, including whitespace and line endings.
                """);
        }

        if (expectedReplacements >= 0 && occurrences != expectedReplacements)
        {
            return ToolResult.Failure($"Expected {expectedReplacements} replacement(s) in {path}, but found {occurrences} occurrence(s).");
        }

        if (!replaceAll && occurrences > 1)
        {
            return ToolResult.Failure($"Found {occurrences} occurrences in {path}. Set replace_all=true or provide more specific old_text.");
        }

        var updated = replaceAll
            ? content.Replace(oldText, newText, StringComparison.Ordinal)
            : ReplaceFirst(content, oldText, newText);

        await File.WriteAllTextAsync(resolved, updated, cancellationToken).ConfigureAwait(false);

        var replacements = replaceAll ? occurrences : 1;
        return ToolResult.Success($"Replaced {replacements} occurrence(s) in {path}.");
    }

    private static int CountOccurrences(string content, string oldText)
    {
        var count = 0;
        var index = 0;

        while ((index = content.IndexOf(oldText, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += oldText.Length;
        }

        return count;
    }

    private static string ReplaceFirst(string content, string oldText, string newText)
    {
        var index = content.IndexOf(oldText, StringComparison.Ordinal);
        return index < 0
            ? content
            : content[..index] + newText + content[(index + oldText.Length)..];
    }
}
