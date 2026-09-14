using System.Text;
using System.Text.Json.Nodes;
using AgentForge.Mcp.Server;

namespace AgentForge.Capabilities.SqlReader.Tools;

internal sealed class ListTablesTool : McpTool
{
    private readonly SqliteRoot _root;

    public ListTablesTool(SqliteRoot root)
    {
        _root = root;
    }

    public override string Name => "list_tables";

    public override string Description =>
        "Lists all user tables in the SQLite database, one per line. "
        + "Filters out sqlite_* internal tables. Call describe_table next to see columns.";

    public override JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject(),
    };

    public override async Task<string> InvokeAsync(JsonNode? arguments, CancellationToken ct)
    {
        await using var conn = await _root.OpenAsync(ct).ConfigureAwait(false);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT name FROM sqlite_master
            WHERE type = 'table' AND name NOT LIKE 'sqlite_%'
            ORDER BY name;
            """;

        var sb = new StringBuilder();
        var count = 0;
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            sb.Append(reader.GetString(0)).Append('\n');
            count++;
        }

        return count == 0
            ? "(no user tables — database is empty or contains only sqlite_ internals)"
            : sb.ToString().TrimEnd('\n');
    }
}
