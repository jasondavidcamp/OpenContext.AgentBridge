using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenContext.AgentBridge.SimulatedGateway;

public sealed class SimulatedGatewayResponder
{
    private const string ModelId = "simulated-gemini-flash";
    private const string SandboxReadmePath = "examples/powershell-sandbox/README.md";
    private const string SandboxScriptPath = "examples/powershell-sandbox/Get-Greeting.ps1";
    private const string SandboxDotNetProgramPath = "examples/sandbox-project/SandboxApp/Program.cs";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SimulatedChatCompletionResponse CreateChatCompletion(string requestJson)
    {
        var request = SimulatedChatRequest.Parse(requestJson);
        var content = CreateAssistantContent(request.Messages);

        return new SimulatedChatCompletionResponse(
            $"chatcmpl-sim-{Guid.NewGuid():N}",
            "chat.completion",
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            request.Model ?? ModelId,
            new[]
            {
                new SimulatedChatChoice(
                    0,
                    new SimulatedChatMessage("assistant", content),
                    "stop")
            },
            new SimulatedUsage(0, 0, 0));
    }

    public string CreateAssistantContent(IReadOnlyList<SimulatedChatMessage> messages)
    {
        var transcript = string.Join('\n', messages.Select(message => message.Content));
        if (Contains(transcript, "model test ok"))
        {
            return Final("""model test ok""");
        }

        if (Contains(transcript, "Improve the PowerShell script help text"))
        {
            return CreateEditSmokeResponse(messages);
        }

        if (Contains(transcript, "Use the workspace map to find the C# greeting implementation"))
        {
            return CreateSymbolAwareEditSmokeResponse(messages);
        }

        if (Contains(transcript, "Only inspect examples/powershell-sandbox"))
        {
            return CreateNoEditSmokeResponse(messages);
        }

        if (!HasAssistantTool(messages, "read_file", "README.md"))
        {
            return Tool("read_file", new Dictionary<string, object?>
            {
                ["path"] = "README.md"
            });
        }

        return Final("Simulated endpoint completed the request after inspecting README.md.");
    }

    private static string CreateNoEditSmokeResponse(IReadOnlyList<SimulatedChatMessage> messages)
    {
        if (!HasAssistantTool(messages, "read_file", SandboxReadmePath))
        {
            return Tool("read_file", new Dictionary<string, object?>
            {
                ["path"] = SandboxReadmePath
            });
        }

        if (!HasAssistantTool(messages, "read_file", SandboxScriptPath))
        {
            return Tool("read_file", new Dictionary<string, object?>
            {
                ["path"] = SandboxScriptPath
            });
        }

        return Final("Inspected the PowerShell sandbox README and greeting script without making changes.");
    }

    private static string CreateEditSmokeResponse(IReadOnlyList<SimulatedChatMessage> messages)
    {
        if (!HasAssistantTool(messages, "read_file", SandboxScriptPath))
        {
            return Tool("read_file", new Dictionary<string, object?>
            {
                ["path"] = SandboxScriptPath
            });
        }

        if (!HasAssistantTool(messages, "replace_text", SandboxScriptPath))
        {
            return Tool("replace_text", new Dictionary<string, object?>
            {
                ["path"] = SandboxScriptPath,
                ["old_text"] = "[CmdletBinding()]",
                ["new_text"] = """
                    <#
                    .SYNOPSIS
                    Writes a simple greeting for the supplied name.
                    #>
                    [CmdletBinding()]
                    """,
                ["replace_all"] = false,
                ["expected_replacements"] = 1
            });
        }

        if (!HasAssistantTool(messages, "run_command"))
        {
            return Tool("run_command", new Dictionary<string, object?>
            {
                ["command"] = @"pwsh -NoProfile -File .\examples\powershell-sandbox\Get-Greeting.ps1 -Name AgentBridge",
                ["timeout_minutes"] = 2
            });
        }

        if (!HasAssistantTool(messages, "git_diff", SandboxScriptPath))
        {
            return Tool("git_diff", new Dictionary<string, object?>
            {
                ["path"] = SandboxScriptPath
            });
        }

        return Final("Updated the script help text, validated the greeting command, and reviewed the resulting diff.");
    }

    private static string CreateSymbolAwareEditSmokeResponse(IReadOnlyList<SimulatedChatMessage> messages)
    {
        if (!HasAssistantTool(messages, "read_file", SandboxDotNetProgramPath))
        {
            return Tool("read_file", new Dictionary<string, object?>
            {
                ["path"] = SandboxDotNetProgramPath
            });
        }

        if (!HasAssistantTool(messages, "replace_text", SandboxDotNetProgramPath))
        {
            return Tool("replace_text", new Dictionary<string, object?>
            {
                ["path"] = SandboxDotNetProgramPath,
                ["old_text"] = "return $\"Hello, {name}!\";",
                ["new_text"] = "return $\"Hello, {name} from AgentBridge!\";",
                ["replace_all"] = false,
                ["expected_replacements"] = 1
            });
        }

        if (!HasAssistantTool(messages, "run_command", "SandboxApp"))
        {
            return Tool("run_command", new Dictionary<string, object?>
            {
                ["command"] = @"dotnet run --project .\examples\sandbox-project\SandboxApp -- AgentBridge",
                ["timeout_minutes"] = 5
            });
        }

        if (!HasAssistantTool(messages, "git_diff", SandboxDotNetProgramPath))
        {
            return Tool("git_diff", new Dictionary<string, object?>
            {
                ["path"] = SandboxDotNetProgramPath
            });
        }

        return Final("Found the C# greeting implementation from the workspace map, updated it, validated the app, and reviewed the diff.");
    }

    private static bool HasAssistantTool(
        IReadOnlyList<SimulatedChatMessage> messages,
        string toolName,
        string? path = null)
    {
        return messages.Any(message =>
            string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)
            && Contains(message.Content, """"type":"tool"""")
            && Contains(message.Content, $""""tool":"{toolName}"""")
            && (path is null || Contains(message.Content, path)));
    }

    private static string Tool(string name, IReadOnlyDictionary<string, object?> arguments)
    {
        return JsonSerializer.Serialize(
            new Dictionary<string, object?>
            {
                ["type"] = "tool",
                ["tool"] = name,
                ["arguments"] = arguments
            },
            JsonOptions);
    }

    private static string Final(string message)
    {
        return JsonSerializer.Serialize(
            new Dictionary<string, object?>
            {
                ["type"] = "final",
                ["message"] = message
            },
            JsonOptions);
    }

    private static bool Contains(string value, string expected)
    {
        return value.Contains(expected, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record SimulatedChatCompletionResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("object")] string Object,
    [property: JsonPropertyName("created")] long Created,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("choices")] IReadOnlyList<SimulatedChatChoice> Choices,
    [property: JsonPropertyName("usage")] SimulatedUsage Usage);

public sealed record SimulatedChatChoice(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("message")] SimulatedChatMessage Message,
    [property: JsonPropertyName("finish_reason")] string FinishReason);

public sealed record SimulatedChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

public sealed record SimulatedUsage(
    [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
    [property: JsonPropertyName("completion_tokens")] int CompletionTokens,
    [property: JsonPropertyName("total_tokens")] int TotalTokens);

public sealed record SimulatedModelsResponse(
    [property: JsonPropertyName("object")] string Object,
    [property: JsonPropertyName("data")] IReadOnlyList<SimulatedModelObject> Data)
{
    public static SimulatedModelsResponse Create()
    {
        return new SimulatedModelsResponse(
            "list",
            new[]
            {
                new SimulatedModelObject(
                    "simulated-gemini-flash",
                    "model",
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    "agentbridge-simulator"),
                new SimulatedModelObject(
                    "simulated-gemini-pro",
                    "model",
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    "agentbridge-simulator")
            });
    }
}

public sealed record SimulatedModelObject(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("object")] string Object,
    [property: JsonPropertyName("created")] long Created,
    [property: JsonPropertyName("owned_by")] string OwnedBy);

public sealed record SimulatedChatRequest(
    string? Model,
    IReadOnlyList<SimulatedChatMessage> Messages)
{
    public static SimulatedChatRequest Parse(string requestJson)
    {
        using var document = JsonDocument.Parse(requestJson);
        var root = document.RootElement;
        var model = root.TryGetProperty("model", out var modelElement)
            ? modelElement.GetString()
            : null;
        var messages = new List<SimulatedChatMessage>();

        if (root.TryGetProperty("messages", out var messagesElement)
            && messagesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var messageElement in messagesElement.EnumerateArray())
            {
                var role = messageElement.TryGetProperty("role", out var roleElement)
                    ? roleElement.GetString()
                    : null;
                var content = messageElement.TryGetProperty("content", out var contentElement)
                    ? contentElement.GetString()
                    : null;

                if (!string.IsNullOrWhiteSpace(role) && content is not null)
                {
                    messages.Add(new SimulatedChatMessage(role, content));
                }
            }
        }

        return new SimulatedChatRequest(model, messages);
    }
}
