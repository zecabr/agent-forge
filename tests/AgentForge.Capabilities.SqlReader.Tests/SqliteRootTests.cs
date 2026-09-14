using AgentForge.Capabilities.SqlReader;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AgentForge.Capabilities.SqlReader.Tests;

public class SqliteRootTests : IDisposable
{
    private readonly string _dbPath;

    public SqliteRootTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "sqlreader-root-" + Guid.NewGuid().ToString("N") + ".db");
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE users(id INTEGER PRIMARY KEY, name TEXT);
            INSERT INTO users(id, name) VALUES (1, 'alice'), (2, 'bob');
            """;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { /* best-effort */ }
    }

    [Fact]
    public void Ctor_Rejects_Missing_File()
    {
        Assert.Throws<FileNotFoundException>(() =>
            new SqliteRoot(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".db")));
    }

    [Fact]
    public void Ctor_Rejects_Null_Or_Whitespace()
    {
        Assert.Throws<ArgumentException>(() => new SqliteRoot(""));
        Assert.Throws<ArgumentException>(() => new SqliteRoot("   "));
    }

    [Fact]
    public async Task OpenAsync_Opens_Connection_And_Query_Only_Is_Set()
    {
        var root = new SqliteRoot(_dbPath);
        await using var conn = await root.OpenAsync(CancellationToken.None);

        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA query_only;";
        var value = await pragma.ExecuteScalarAsync();

        Assert.Equal(1L, Assert.IsType<long>(value));
    }

    [Fact]
    public async Task ReadOnly_Rejects_Insert()
    {
        var root = new SqliteRoot(_dbPath);
        await using var conn = await root.OpenAsync(CancellationToken.None);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO users(id, name) VALUES (99, 'evil');";

        var ex = await Assert.ThrowsAsync<SqliteException>(() => cmd.ExecuteNonQueryAsync());
        // SQLite responde SQLITE_READONLY (código 8) ou mensagem contendo "read-only"/"readonly".
        Assert.True(ex.Message.Contains("read", StringComparison.OrdinalIgnoreCase)
                    || ex.SqliteErrorCode == 8,
            $"Expected read-only rejection, got: {ex.Message}");
    }

    [Fact]
    public async Task ReadOnly_Rejects_CTE_With_Insert()
    {
        // Defesa em profundidade: PRAGMA query_only bloqueia INSERT mesmo dentro de CTE.
        var root = new SqliteRoot(_dbPath);
        await using var conn = await root.OpenAsync(CancellationToken.None);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            WITH new_user AS (SELECT 42 AS id, 'sneaky' AS name)
            INSERT INTO users(id, name) SELECT id, name FROM new_user;
            """;

        await Assert.ThrowsAsync<SqliteException>(() => cmd.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task ReadOnly_Rejects_DDL()
    {
        var root = new SqliteRoot(_dbPath);
        await using var conn = await root.OpenAsync(CancellationToken.None);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DROP TABLE users;";

        await Assert.ThrowsAsync<SqliteException>(() => cmd.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task Select_Works_Normally()
    {
        var root = new SqliteRoot(_dbPath);
        await using var conn = await root.OpenAsync(CancellationToken.None);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM users;";
        var count = (long)(await cmd.ExecuteScalarAsync())!;

        Assert.Equal(2, count);
    }
}
