namespace OpenContext.AgentBridge.Core;

public static class AgentBridgeDefaults
{
    public const string LocalStateDirectoryName = ".agentbridge";
    public const string SkillsDirectoryName = "skills";
    public const string ConversationDatabaseFileName = "agentbridge.db";
    public const string ConfigFileName = "config.json";
    public const string LogsDirectoryName = "logs";
    public const string DefaultDockerImage = "opencontext-agentbridge-tools:latest";
    public const string DefaultModelProvider = "gemini";
    public const string DefaultGeminiModel = "gemini-1.5-pro";
    public const string DefaultOpenAiCompatibleModel = "gpt-4";
    public const string DefaultOpenAiCompatibleApiKeyHeader = "Authorization";
    public const string DefaultOpenAiCompatibleApiKeyPrefix = "Bearer";
    public const string DefaultExecutor = "host";
    public const int DefaultMaxIterations = 8;
}
