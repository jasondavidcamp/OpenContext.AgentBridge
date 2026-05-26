namespace OpenContext.AgentBridge.Core.Tools;

internal static class ToolText
{
    public static string Truncate(string value, int maxCharacters = 20_000)
    {
        if (value.Length <= maxCharacters)
        {
            return value;
        }

        return value[..maxCharacters] + $"{Environment.NewLine}[truncated after {maxCharacters} characters]";
    }
}
