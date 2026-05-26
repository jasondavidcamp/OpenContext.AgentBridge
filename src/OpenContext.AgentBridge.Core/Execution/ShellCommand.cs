using System.Runtime.InteropServices;

namespace OpenContext.AgentBridge.Core.Execution;

public static class ShellCommand
{
    public static CommandRequest Create(string command, TimeSpan? timeout = null)
    {
        return Create(command, "host", timeout);
    }

    public static CommandRequest Create(string command, string executorName, TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ArgumentException("A shell command is required.", nameof(command));
        }

        if (string.Equals(executorName, "docker", StringComparison.OrdinalIgnoreCase))
        {
            return CommandRequest.Create(
                "/bin/bash",
                new[] { "-lc", command },
                timeout);
        }

        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? CommandRequest.Create(
                "powershell",
                new[] { "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", command },
                timeout)
            : CommandRequest.Create(
                "/bin/bash",
                new[] { "-lc", command },
                timeout);
    }
}
