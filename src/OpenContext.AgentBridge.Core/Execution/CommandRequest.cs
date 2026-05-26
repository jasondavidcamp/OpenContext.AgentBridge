namespace OpenContext.AgentBridge.Core.Execution;

public sealed record CommandRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    TimeSpan? Timeout = null,
    IReadOnlyDictionary<string, string?>? Environment = null)
{
    public static CommandRequest Create(
        string fileName,
        IEnumerable<string>? arguments = null,
        TimeSpan? timeout = null,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        return new CommandRequest(
            fileName,
            arguments?.ToArray() ?? Array.Empty<string>(),
            timeout,
            environment);
    }
}
