namespace OpenContext.AgentBridge.Core.Models;

public interface IModelProvider
{
    string Name { get; }

    Task<AgentTurnResponse> CompleteAsync(
        AgentTurnRequest request,
        CancellationToken cancellationToken = default);
}
