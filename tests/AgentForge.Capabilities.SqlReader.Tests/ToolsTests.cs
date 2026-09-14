using System.Text.Json.Nodes;
using AgentForge.Capabilities.SqlReader;
using AgentForge.Capabilities.SqlReader.Tools;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AgentForge.Capabilities.SqlReader.Tests;

public class ToolsTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteRoot _root;

    public ToolsTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "sqlreader-tools-" + Guid.NewGuid().ToString("N") + ".db");
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE customer(
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                city TEXT DEFAULT 'unknown'
            );
            CREATE TABLE orders(id INTEGER PRIMARY KEY, customer_id INTEGER, amount REAL);
            INSERT INTO customer(id, name, city) VALUES
                (1, 'Zeca', 'Blumenau'),
                (2, 'Alice', 'Curitiba'),
                (3, 'Bob', NULL);
            INSERT INTO orders(id, customer_id, amount) VALUES
                (1, 1, 100.50),
                (2, 1, 200.00),
                (3, 2, 50.25);
            """;
        cmd.ExecuteNonQuery();

        _root = new SqliteRoot(_dbPath);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { /* best-effort */ }
    }

    [Fact]
    public async Task ListTables_Returns_User_Tables()
    {
        var tool = new ListTablesTool(_root);
        var text = await tool.InvokeAsync(arguments: null, CancellationToken.None);
        var lines = text.Split('\n');

        Assert.Contains("customer", lines);
        Assert.Contains("orders", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("sqlite_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DescribeTable_Returns_Schema()
    {
        var tool = new DescribeTableTool(_root);
        var args = new JsonObject { ["table"] = "customer" };
        var text = await tool.InvokeAsync(args, CancellationToken.None);

        // header + 3 columns = 4 linhas
        var lines = text.Split('\n');
        Assert.StartsWith("cid\tname\ttype\tnotnull\tdflt_value\tpk", lines[0]);
        Assert.Contains(lines, l => l.Contains("\tid\t", StringComparison.Ordinal) && l.EndsWith("\t1", StringComparison.Ordinal)); // pk=1
        Assert.Contains(lines, l => l.Contains("\tname\t", StringComparison.Ordinal) && l.Contains("\t1\t", StringComparison.Ordinal)); // notnull=1
        Assert.Contains(lines, l => l.Contains("\tcity\t", StringComparison.Ordinal) && l.Contains("'unknown'", StringComparison.Ordinal)); // default
    }

    [Fact]
    public async Task DescribeTable_Rejects_Missing_Table()
    {
        var tool = new DescribeTableTool(_root);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            tool.InvokeAsync(new JsonObject(), CancellationToken.None));
    }

    [Fact]
    public async Task DescribeTable_Rejects_Unknown_Table()
    {
        var tool = new DescribeTableTool(_root);
        var args = new JsonObject { ["table"] = "does_not_exist" };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tool.InvokeAsync(args, CancellationToken.None));
    }

    [Fact]
    public async Task Query_Returns_Rows_As_Tsv_With_Header()
    {
        var tool = new QueryTool(_root);
        var args = new JsonObject { ["sql"] = "SELECT id, name FROM customer ORDER BY id" };
        var text = await tool.InvokeAsync(args, CancellationToken.None);
        var lines = text.Split('\n');

        Assert.Equal("id\tname", lines[0]);
        Assert.Equal("1\tZeca", lines[1]);
        Assert.Equal("2\tAlice", lines[2]);
        Assert.Equal("3\tBob", lines[3]);
        Assert.Contains("(3 rows)", lines[4]);
    }

    [Fact]
    public async Task Query_Handles_Null_As_Literal_NULL()
    {
        var tool = new QueryTool(_root);
        var args = new JsonObject { ["sql"] = "SELECT city FROM customer WHERE id = 3" };
        var text = await tool.InvokeAsync(args, CancellationToken.None);

        Assert.Contains("NULL", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Query_Escapes_Tab_And_Newline_In_Cells()
    {
        var tool = new QueryTool(_root);
        var args = new JsonObject
        {
            ["sql"] = "SELECT 'a\tb' AS tabbed, 'x" + "\n" + "y' AS newlined",
        };
        var text = await tool.InvokeAsync(args, CancellationToken.None);

        Assert.Contains("a\\tb", text, StringComparison.Ordinal);
        Assert.Contains("x\\ny", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Query_Rejects_Insert_Via_Engine()
    {
        var tool = new QueryTool(_root);
        var args = new JsonObject { ["sql"] = "INSERT INTO customer(id, name) VALUES (99, 'evil')" };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tool.InvokeAsync(args, CancellationToken.None));
    }

    [Fact]
    public async Task Query_Rejects_Update()
    {
        var tool = new QueryTool(_root);
        var args = new JsonObject { ["sql"] = "UPDATE customer SET name='x' WHERE id=1" };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tool.InvokeAsync(args, CancellationToken.None));
    }

    [Fact]
    public async Task Query_Rejects_Delete()
    {
        var tool = new QueryTool(_root);
        var args = new JsonObject { ["sql"] = "DELETE FROM customer" };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tool.InvokeAsync(args, CancellationToken.None));
    }

    [Fact]
    public async Task Query_Rejects_Drop_Table()
    {
        var tool = new QueryTool(_root);
        var args = new JsonObject { ["sql"] = "DROP TABLE customer" };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tool.InvokeAsync(args, CancellationToken.None));
    }

    [Fact]
    public async Task Query_Empty_Result_Returns_Zero_Rows_Marker()
    {
        var tool = new QueryTool(_root);
        var args = new JsonObject { ["sql"] = "SELECT * FROM customer WHERE id = 999" };
        var text = await tool.InvokeAsync(args, CancellationToken.None);

        Assert.Contains("(0 rows)", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Query_Rejects_Missing_Sql()
    {
        var tool = new QueryTool(_root);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            tool.InvokeAsync(new JsonObject(), CancellationToken.None));
    }

    [Fact]
    public async Task Query_Rejects_Empty_Sql()
    {
        var tool = new QueryTool(_root);
        var args = new JsonObject { ["sql"] = "   " };
        await Assert.ThrowsAsync<ArgumentException>(() =>
            tool.InvokeAsync(args, CancellationToken.None));
    }
}
