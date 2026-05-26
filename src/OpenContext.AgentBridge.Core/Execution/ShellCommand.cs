using System.Runtime.InteropServices;

namespace OpenContext.AgentBridge.Core.Execution;

public static class ShellCommand
{
    public static CommandRequest Create(string command, TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ArgumentException("A shell command is required.", nameof(command));
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
