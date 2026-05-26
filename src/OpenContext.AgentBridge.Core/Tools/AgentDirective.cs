using System.Text.Json.Nodes;

namespace OpenContext.AgentBridge.Core.Tools;

public sealed record AgentDirective(
    string Type,
    string? ToolName,
    JsonObject Arguments,
    string? Message);
