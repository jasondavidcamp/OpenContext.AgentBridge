using OpenContext.AgentBridge.Core.Workspaces;

namespace OpenContext.AgentBridge.Core.Execution;

public sealed class DockerWorkspaceExecutor : IWorkspaceExecutor
{
    private readonly HostWorkspaceExecutor _hostExecutor = new();
    private readonly string _image;

    public DockerWorkspaceExecutor(string? image = null)
    {
        _image = string.IsNullOrWhiteSpace(image)
            ? AgentBridgeDefaults.DefaultDockerImage
            : image;
    }

    public string Name => "docker";

    public Task<CommandResult> RunAsync(
        WorkspaceContext workspace,
        CommandRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(request);

        var dockerArguments = new List<string>
        {
            "run",
            "--rm",
            "-i",
            "-v",
            $"{workspace.RootPath}:/workspace",
            "-w",
            "/workspace",
            _image,
            request.FileName
        };

        dockerArguments.AddRange(request.Arguments);

        return _hostExecutor.RunAsync(
            workspace,
            new CommandRequest(
                "docker",
                dockerArguments,
                request.Timeout,
                request.Environment),
            cancellationToken);
    }
}
