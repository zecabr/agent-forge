using Microsoft.Data.Sqlite;

namespace AgentForge.Capabilities.SqlReader;

/// <summary>
/// Encapsula acesso read-only a um arquivo SQLite. Defesa em profundidade:
/// <list type="number">
///   <item>Connection string com <c>Mode=ReadOnly</c> — SQLite recusa qualquer write.</item>
///   <item><c>PRAGMA query_only = 1</c> após open — bloqueia writes mesmo se o modo mudar.</item>
/// </list>
/// </summary>
internal sealed class SqliteRoot
{
    public string DbPath { get; }
    public string ConnectionString { get; }

    public SqliteRoot(string dbPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);

        var absolute = Path.GetFullPath(dbPath);
        if (!File.Exists(absolute))
        {
            throw new FileNotFoundException($"SQLite database not found: {absolute}");
        }

        DbPath = absolute;
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = absolute,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString();
    }

    /// <summary>Abre nova conexão read-only e aplica <c>PRAGMA query_only = 1</c>.</summary>
    public async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new SqliteConnection(ConnectionString);
        try
        {
            await conn.OpenAsync(ct).ConfigureAwait(false);

            using var pragma = conn.CreateCommand();
            pragma.CommandText = "PRAGMA query_only = 1;";
            await pragma.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            return conn;
        }
        catch
        {
            await conn.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
