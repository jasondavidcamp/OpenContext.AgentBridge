using System.Text.Json;
using OpenContext.AgentBridge.Core.Tools;
using OpenContext.AgentBridge.SimulatedGateway;

namespace OpenContext.AgentBridge.Tests;

public sealed class SimulatedGatewayResponderTests
{
    [Fact]
    public void CreateAssistantContent_returns_model_test_response()
    {
        var responder = new SimulatedGatewayResponder();

        var content = responder.CreateAssistantContent(new[]
        {
            new SimulatedChatMessage("user", """Return exactly {"type":"final","message":"model test ok"}.""")
        });

        Assert.True(AgentResponseParser.TryParse(content, out var directive));
        Assert.Equal("final", directive.Type);
        Assert.Equal("model test ok", directive.Message);
    }

    [Fact]
    public void CreateAssistantContent_drives_no_edit_smoke_through_two_reads()
    {
        var responder = new SimulatedGatewayResponder();
        var messages = new List<SimulatedChatMessage>
        {
            new("user", "Only inspect examples/powershell-sandbox. Read the README.md and Get-Greeting.ps1.")
        };

        var first = AppendAssistant(responder, messages);
        Assert.Equal("tool", first.Type);
        Assert.Equal("read_file", first.ToolName);
        Assert.Equal("examples/powershell-sandbox/README.md", GetString(first, "path"));
        messages.Add(ToolResultMessage(first));

        var second = AppendAssistant(responder, messages);
        Assert.Equal("tool", second.Type);
        Assert.Equal("read_file", second.ToolName);
        Assert.Equal("examples/powershell-sandbox/Get-Greeting.ps1", GetString(second, "path"));
        messages.Add(ToolResultMessage(second));

        var final = AppendAssistant(responder, messages);
        Assert.Equal("final", final.Type);
        Assert.Contains("without making changes", final.Message);
    }

    [Fact]
    public void CreateAssistantContent_drives_edit_smoke_through_validation_and_diff()
    {
        var responder = new SimulatedGatewayResponder();
        var messages = new List<SimulatedChatMessage>
        {
            new("user", "Only work in examples/powershell-sandbox. Improve the PowerShell script help text in Get-Greeting.ps1 without changing runtime behavior.")
        };

        var read = AppendAssistant(responder, messages);
        Assert.Equal("read_file", read.ToolName);
        messages.Add(ToolResultMessage(read));

        var replace = AppendAssistant(responder, messages);
        Assert.Equal("replace_text", replace.ToolName);
        Assert.Equal("examples/powershell-sandbox/Get-Greeting.ps1", GetString(replace, "path"));
        Assert.Contains(".SYNOPSIS", GetString(replace, "new_text"));
        messages.Add(ToolResultMessage(replace));

        var validate = AppendAssistant(responder, messages);
        Assert.Equal("run_command", validate.ToolName);
        Assert.Contains("Get-Greeting.ps1 -Name AgentBridge", GetString(validate, "command"));
        messages.Add(ToolResultMessage(validate));

        var diff = AppendAssistant(responder, messages);
        Assert.Equal("git_diff", diff.ToolName);
        Assert.Equal("examples/powershell-sandbox/Get-Greeting.ps1", GetString(diff, "path"));
        messages.Add(ToolResultMessage(diff));

        var final = AppendAssistant(responder, messages);
        Assert.Equal("final", final.Type);
        Assert.Contains("validated", final.Message);
    }

    [Fact]
    public void CreateAssistantContent_drives_symbol_aware_edit_smoke_through_validation_and_diff()
    {
        var responder = new SimulatedGatewayResponder();
        var messages = new List<SimulatedChatMessage>
        {
            new(
                "system",
                "Workspace map: Code symbols: examples/sandbox-project/SandboxApp/Program.cs: string Greeter.CreateGreeting(string name)"),
            new(
                "user",
                "Use the workspace map to find the C# greeting implementation. Modify it so the generated greeting includes the phrase 'from AgentBridge'.")
        };

        var read = AppendAssistant(responder, messages);
        Assert.Equal("read_file", read.ToolName);
        Assert.Equal("examples/sandbox-project/SandboxApp/Program.cs", GetString(read, "path"));
        messages.Add(ToolResultMessage(read));

        var replace = AppendAssistant(responder, messages);
        Assert.Equal("replace_text", replace.ToolName);
        Assert.Equal("examples/sandbox-project/SandboxApp/Program.cs", GetString(replace, "path"));
        Assert.Contains("from AgentBridge", GetString(replace, "new_text"));
        messages.Add(ToolResultMessage(replace));

        var validate = AppendAssistant(responder, messages);
        Assert.Equal("run_command", validate.ToolName);
        Assert.Contains("dotnet run --project", GetString(validate, "command"));
        messages.Add(ToolResultMessage(validate));

        var diff = AppendAssistant(responder, messages);
        Assert.Equal("git_diff", diff.ToolName);
        Assert.Equal("examples/sandbox-project/SandboxApp/Program.cs", GetString(diff, "path"));
        messages.Add(ToolResultMessage(diff));

        var final = AppendAssistant(responder, messages);
        Assert.Equal("final", final.Type);
        Assert.Contains("workspace map", final.Message);
    }

    [Fact]
    public void CreateChatCompletion_returns_openai_compatible_shape()
    {
        var responder = new SimulatedGatewayResponder();
        var requestJson = JsonSerializer.Serialize(new
        {
            model = "simulated-gemini-flash",
            messages = new[]
            {
                new { role = "user", content = """Return exactly {"type":"final","message":"model test ok"}.""" }
            }
        });

        var response = responder.CreateChatCompletion(requestJson);

        Assert.Equal("chat.completion", response.Object);
        Assert.Equal("simulated-gemini-flash", response.Model);
        var choice = Assert.Single(response.Choices);
        Assert.Equal("assistant", choice.Message.Role);
        Assert.Contains("model test ok", choice.Message.Content);
    }

    private static AgentDirective AppendAssistant(
        SimulatedGatewayResponder responder,
        List<SimulatedChatMessage> messages)
    {
        var content = responder.CreateAssistantContent(messages);
        messages.Add(new SimulatedChatMessage("assistant", content));

        Assert.True(AgentResponseParser.TryParse(content, out var directive));
        return directive;
    }

    private static string? GetString(AgentDirective directive, string propertyName)
    {
        return directive.Arguments[propertyName]?.GetValue<string>();
    }

    private static SimulatedChatMessage ToolResultMessage(AgentDirective directive)
    {
        return new SimulatedChatMessage(
            "user",
            $"""
            TOOL_RESULT
            tool: {directive.ToolName}
            success: True
            arguments: {directive.Arguments.ToJsonString()}

            simulated tool result
            """);
    }
}
