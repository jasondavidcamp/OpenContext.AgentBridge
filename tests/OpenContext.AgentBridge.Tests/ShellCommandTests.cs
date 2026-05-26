using OpenContext.AgentBridge.Core.Execution;

namespace OpenContext.AgentBridge.Tests;

public sealed class ShellCommandTests
{
    [Fact]
    public void Create_uses_bash_for_docker_executor()
    {
        var request = ShellCommand.Create("dotnet --info", "docker");

        Assert.Equal("/bin/bash", request.FileName);
        Assert.Equal(new[] { "-lc", "dotnet --info" }, request.Arguments);
    }
}
