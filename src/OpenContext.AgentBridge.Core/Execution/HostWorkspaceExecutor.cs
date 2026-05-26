using System.Diagnostics;
using System.Text;
using OpenContext.AgentBridge.Core.Workspaces;

namespace OpenContext.AgentBridge.Core.Execution;

public sealed class HostWorkspaceExecutor : IWorkspaceExecutor
{
    public string Name => "host";

    public async Task<CommandResult> RunAsync(
        WorkspaceContext workspace,
        CommandRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(request);

        using var timeoutSource = request.Timeout is null
            ? null
            : new CancellationTokenSource(request.Timeout.Value);
        using var linkedSource = timeoutSource is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        var effectiveToken = linkedSource?.Token ?? cancellationToken;
        var output = new StringBuilder();
        var error = new StringBuilder();
        var stopwatch = Stopwatch.StartNew();

        using var process = new Process
        {
            StartInfo = BuildStartInfo(workspace, request),
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
            {
                output.AppendLine(eventArgs.Data);
            }
        };

        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
            {
                error.AppendLine(eventArgs.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var timedOut = false;

        try
        {
            await process.WaitForExitAsync(effectiveToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            timedOut = true;

            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Process exited after the timeout fired.
            }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        stopwatch.Stop();

        return new CommandResult(
            process.HasExited ? process.ExitCode : -1,
            output.ToString(),
            error.ToString(),
            stopwatch.Elapsed,
            timedOut);
    }

    private static ProcessStartInfo BuildStartInfo(WorkspaceContext workspace, CommandRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            WorkingDirectory = workspace.RootPath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (request.Environment is not null)
        {
            foreach (var variable in request.Environment)
            {
                startInfo.Environment[variable.Key] = variable.Value;
            }
        }

        return startInfo;
    }
}
