using OpenContext.AgentBridge.Core.Conversation;
using OpenContext.AgentBridge.Core.Models;
using OpenContext.AgentBridge.Core.Skills;
using OpenContext.AgentBridge.Core.Tools;
using OpenContext.AgentBridge.Core.Workspaces;

namespace OpenContext.AgentBridge.Tests;

public sealed class AgentSystemPromptBuilderTests
{
    [Fact]
    public void Build_includes_workspace_map_when_available()
    {
        var map = new WorkspaceMap(
            "SampleRepo",
            "clean",
            new[] { "src" },
            new[] { "README.md" },
            new[] { "Sample.sln" },
            new[] { "src/Sample/Sample.csproj" },
            Array.Empty<string>(),
            new[] { "src/Sample/Program.cs" },
            new[] { "src/Sample/Program.cs: class Program" },
            Array.Empty<string>(),
            new[] { "skills/powershell.md" },
            new[] { "docs/usage.md" });

        var prompt = AgentSystemPromptBuilder.Build(
            new AgentTurnRequest(
                "C:\\sample",
                new[]
                {
                    new AgentMessage("user", "Inspect this repo.", DateTimeOffset.UtcNow)
                },
                new[]
                {
                    new Skill("PowerShell", "skills/powershell.md", "Use PowerShell carefully.")
                },
                new[]
                {
                    new ToolDefinition("read_file", "Read a file.", "{}")
                },
                map));

        Assert.Contains("Workspace map:", prompt);
        Assert.Contains("Root name: SampleRepo", prompt);
        Assert.Contains("Projects: src/Sample/Sample.csproj", prompt);
        Assert.Contains("Likely entry points: src/Sample/Program.cs", prompt);
        Assert.Contains("Code symbols: src/Sample/Program.cs: class Program", prompt);
        Assert.Contains("# Skill: PowerShell", prompt);
        Assert.Contains("- read_file: Read a file.", prompt);
        Assert.Contains("When adding text before or after an exact existing line", prompt);
        Assert.Contains("If apply_patch fails for a file", prompt);
    }
}
