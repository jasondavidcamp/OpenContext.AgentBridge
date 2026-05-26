using System.Net.Http.Json;
using System.Text.Json;
using OpenContext.AgentBridge.Core.Conversation;
using OpenContext.AgentBridge.Core.Models;

namespace OpenContext.AgentBridge.Providers.Gemini;

public sealed class GeminiModelProvider : IModelProvider
{
    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _options;

    public GeminiModelProvider(HttpClient httpClient, GeminiOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public string Name => "gemini";

    public async Task<AgentTurnResponse> CompleteAsync(
        AgentTurnRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) && string.IsNullOrWhiteSpace(_options.Endpoint))
        {
            throw new InvalidOperationException(
                "Gemini is not configured. Set AGENTBRIDGE_GEMINI_API_KEY or AGENTBRIDGE_GEMINI_ENDPOINT.");
        }

        var endpoint = _options.GetEndpoint();
        var payload = new
        {
            system_instruction = new
            {
                parts = new[]
                {
                    new { text = BuildSystemInstructions(request) }
                }
            },
            contents = request.Messages.Select(ToGeminiContent).ToArray()
        };

        using var response = await _httpClient
            .PostAsJsonAsync(endpoint, payload, cancellationToken)
            .ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Gemini request failed with {(int)response.StatusCode}: {body}");
        }

        return new AgentTurnResponse(ExtractText(body));
    }

    private static object ToGeminiContent(AgentMessage message)
    {
        var role = string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)
            ? "model"
            : "user";

        return new
        {
            role,
            parts = new[]
            {
                new { text = message.Content }
            }
        };
    }

    private static string BuildSystemInstructions(AgentTurnRequest request)
    {
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

            Use tools to inspect files before modifying them. Keep all paths relative to the workspace unless a tool says otherwise.
            After a tool result, either request the next tool action or return a final JSON object.

            Loaded skills:
            {skillText}
            """;
    }

    private static string ExtractText(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);

        var candidates = document.RootElement.GetProperty("candidates");
        if (candidates.GetArrayLength() == 0)
        {
            return string.Empty;
        }

        var parts = candidates[0]
            .GetProperty("content")
            .GetProperty("parts");

        return string.Join(
            Environment.NewLine,
            parts.EnumerateArray()
                .Where(part => part.TryGetProperty("text", out _))
                .Select(part => part.GetProperty("text").GetString())
                .Where(text => !string.IsNullOrWhiteSpace(text)));
    }
}
