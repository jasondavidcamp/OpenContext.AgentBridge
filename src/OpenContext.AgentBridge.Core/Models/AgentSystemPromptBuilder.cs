namespace OpenContext.AgentBridge.Core.Models;

public static class AgentSystemPromptBuilder
{
    public static string Build(AgentTurnRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var skillText = request.Skills.Count == 0
            ? "No skills are currently loaded."
            : string.Join(
                Environment.NewLine + Environment.NewLine,
                request.Skills.Select(skill => $"# Skill: {skill.Name}{Environment.NewLine}{skill.Instructions}"));
        var toolText = request.Tools.Count == 0
            ? "No tools are currently available."
            : string.Join(
                Environment.NewLine,
                request.Tools.Select(tool => $"- {tool.Name}: {tool.Description} Arguments: {tool.ArgumentsSchema}"));
        const string toolExample = """{"type":"tool","tool":"read_file","arguments":{"path":"README.md"}}""";
        const string finalExample = """{"type":"final","message":"Short summary of the result."}""";

        return $"""
            You are AgentBridge, a workspace-scoped coding agent.
            Workspace root: {request.WorkspaceRoot}

            You must respond with exactly one JSON object and no surrounding prose.

            To request a tool action:
            {toolExample}

            To finish:
            {finalExample}

            Available tools:
            {toolText}

            Use exactly the listed tool names. Do not invent tools such as execute_command or list_directory.
            You do not have direct filesystem access. Never say you searched, inspected, or modified files unless that information came from an AgentBridge tool result.
            If a requested file is missing, use list_files or search inside the workspace; do not infer that the workspace is empty from any external model runtime path.
            Keep all paths relative to the workspace unless a tool says otherwise.
            Use tools to inspect files before modifying them. If the user asked you not to edit files, do not call editing tools.
            For one-line or small exact substitutions, use replace_text after reading the file. Do not use apply_patch for simple exact substitutions. Use apply_patch for multi-line targeted edits. Use write_file only when creating or replacing a whole file is the clearest option.
            Final messages must be concise plain text in the JSON message field. Avoid Markdown headings, tables, code fences, and decorative formatting unless the user explicitly asked for them.
            After a tool result, either request the next tool action or return a final JSON object.

            Loaded skills:
            {skillText}
            """;
    }
}
