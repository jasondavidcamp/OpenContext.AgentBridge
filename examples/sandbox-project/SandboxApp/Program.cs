var name = args.Length == 0
    ? "AgentBridge"
    : string.Join(' ', args);

Console.WriteLine(Greeter.CreateGreeting(name));

public static class Greeter
{
    public static string CreateGreeting(string name)
    {
        return $"Hello, {name}!";
    }
}
