using Microsoft.Data.Sqlite;
using OpenContext.AgentBridge.Core.Conversation;

namespace OpenContext.AgentBridge.Storage;

public sealed class SqliteConversationStore : IConversationStore
{
    private readonly string _databasePath;

    public SqliteConversationStore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("A database path is required.", nameof(databasePath));
        }

        _databasePath = databasePath;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS conversations (
                id TEXT PRIMARY KEY,
                workspace_root TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS messages (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                conversation_id TEXT NOT NULL,
                role TEXT NOT NULL,
                content TEXT NOT NULL,
                created_at TEXT NOT NULL,
                FOREIGN KEY(conversation_id) REFERENCES conversations(id)
            );

            CREATE TABLE IF NOT EXISTS tool_calls (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                conversation_id TEXT NOT NULL,
                tool_name TEXT NOT NULL,
                arguments_json TEXT NOT NULL,
                is_success INTEGER NOT NULL,
                result_content TEXT NOT NULL,
                created_at TEXT NOT NULL,
                FOREIGN KEY(conversation_id) REFERENCES conversations(id)
            );
            """;

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> CreateConversationAsync(
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO conversations (id, workspace_root, created_at, updated_at)
            VALUES ($id, $workspace_root, $created_at, $updated_at);
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$workspace_root", workspaceRoot);
        command.Parameters.AddWithValue("$created_at", now.ToString("O"));
        command.Parameters.AddWithValue("$updated_at", now.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return id;
    }

    public async Task AppendMessageAsync(
        string conversationId,
        AgentMessage message,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO messages (conversation_id, role, content, created_at)
                VALUES ($conversation_id, $role, $content, $created_at);
                """;
            insert.Parameters.AddWithValue("$conversation_id", conversationId);
            insert.Parameters.AddWithValue("$role", message.Role);
            insert.Parameters.AddWithValue("$content", message.Content);
            insert.Parameters.AddWithValue("$created_at", message.CreatedAt.ToString("O"));

            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE conversations
                SET updated_at = $updated_at
                WHERE id = $conversation_id;
                """;
            update.Parameters.AddWithValue("$conversation_id", conversationId);
            update.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.ToString("O"));

            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AgentMessage>> ReadMessagesAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT role, content, created_at
            FROM messages
            WHERE conversation_id = $conversation_id
            ORDER BY id;
            """;
        command.Parameters.AddWithValue("$conversation_id", conversationId);

        var messages = new List<AgentMessage>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            messages.Add(new AgentMessage(
                reader.GetString(0),
                reader.GetString(1),
                DateTimeOffset.Parse(reader.GetString(2))));
        }

        return messages;
    }

    public async Task AppendToolCallAsync(
        string conversationId,
        ToolCallRecord toolCall,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO tool_calls (conversation_id, tool_name, arguments_json, is_success, result_content, created_at)
                VALUES ($conversation_id, $tool_name, $arguments_json, $is_success, $result_content, $created_at);
                """;
            insert.Parameters.AddWithValue("$conversation_id", conversationId);
            insert.Parameters.AddWithValue("$tool_name", toolCall.ToolName);
            insert.Parameters.AddWithValue("$arguments_json", toolCall.ArgumentsJson);
            insert.Parameters.AddWithValue("$is_success", toolCall.IsSuccess ? 1 : 0);
            insert.Parameters.AddWithValue("$result_content", toolCall.ResultContent);
            insert.Parameters.AddWithValue("$created_at", toolCall.CreatedAt.ToString("O"));

            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE conversations
                SET updated_at = $updated_at
                WHERE id = $conversation_id;
                """;
            update.Parameters.AddWithValue("$conversation_id", conversationId);
            update.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.ToString("O"));

            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ToolCallRecord>> ReadToolCallsAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT tool_name, arguments_json, is_success, result_content, created_at
            FROM tool_calls
            WHERE conversation_id = $conversation_id
            ORDER BY id;
            """;
        command.Parameters.AddWithValue("$conversation_id", conversationId);

        var toolCalls = new List<ToolCallRecord>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            toolCalls.Add(new ToolCallRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2) == 1,
                reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4))));
        }

        return toolCalls;
    }

    public async Task<IReadOnlyList<ConversationSummary>> ListConversationsAsync(
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, workspace_root, created_at, updated_at
            FROM conversations
            WHERE workspace_root = $workspace_root
            ORDER BY updated_at DESC;
            """;
        command.Parameters.AddWithValue("$workspace_root", workspaceRoot);

        var conversations = new List<ConversationSummary>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            conversations.Add(new ConversationSummary(
                reader.GetString(0),
                reader.GetString(1),
                DateTimeOffset.Parse(reader.GetString(2)),
                DateTimeOffset.Parse(reader.GetString(3))));
        }

        return conversations;
    }

    private SqliteConnection CreateConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Pooling = false
        };

        return new SqliteConnection(builder.ConnectionString);
    }
}
