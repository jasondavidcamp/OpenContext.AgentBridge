namespace OpenContext.AgentBridge.Core.Agents;

public sealed record AgentLoopOptions(
    int MaxToolIterations = 8,
    IProgress<AgentProgressEvent>? Progress = null,
    int MaxToolObservationCharacters = AgentBridgeDefaults.DefaultMaxToolObservationCharacters,
    int RequiredToolCallsBeforeFinal = 0);
