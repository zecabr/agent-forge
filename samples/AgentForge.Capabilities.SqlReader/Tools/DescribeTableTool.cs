using System.Text;
using System.Text.Json.Nodes;
using AgentForge.Mcp.Server;
using Microsoft.Data.Sqlite;

namespace AgentForge.Capabilities.SqlReader.Tools;

internal sealed class DescribeTableTool : McpTool
{
    private readonly SqliteRoot _root;

    public DescribeTableTool(SqliteRoot root)
    {
        _root = root;
    }

    public override string Name => "describe_table";

    public override string Description =>
        "Returns the schema of a table: column name, declared type, nullability, default, and primary-key flag. "
        + "Use this before writing a query, so you know the exact column names and types.";

    public override JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["table"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Table name (as returned by list_tables).",
            },
        },
        ["required"] = new JsonArray { "table" },
    };

    public override async Task<string> InvokeAsync(JsonNode? arguments, CancellationToken ct)
    {
        var table = arguments?["table"]?.GetValue<string>()
            ?? throw new ArgumentException("Missing required 'table' argument.");

        if (string.IsNullOrWhiteSpace(table))
        {
            throw new ArgumentException("Table name must not be empty.");
        }

        await using var conn = await _root.OpenAsync(ct).ConfigureAwait(false);

        // Valida existência primeiro pra dar mensagem de erro clara.
        using var check = conn.CreateCommand();
        check.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name;";
        check.Parameters.AddWithValue("$name", table);
        var exists = await check.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (exists is null)
        {
            throw new InvalidOperationException(
                $"Table '{table}' not found. Use list_tables to see what exists.");
        }

        // PRAGMA table_info não aceita placeholder — nome vai inline. Como já validamos
        // que existe em sqlite_master, evitamos injeção via nome inexistente. Ainda assim,
        // escapamos aspas duplas no nome pra travar identificadores exóticos.
        var safeTable = table.Replace("\"", "\"\"", StringComparison.Ordinal);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{safeTable}\");";

        var sb = new StringBuilder();
        sb.Append("cid\tname\ttype\tnotnull\tdflt_value\tpk\n");

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            sb.Append(reader.GetInt64(0)).Append('\t');
            sb.Append(reader.GetString(1)).Append('\t');
            sb.Append(reader.GetString(2)).Append('\t');
            sb.Append(reader.GetInt64(3)).Append('\t');
            sb.Append(FormatValue(reader, 4)).Append('\t');
            sb.Append(reader.GetInt64(5)).Append('\n');
        }

        return sb.ToString().TrimEnd('\n');
    }

    private static string FormatValue(SqliteDataReader reader, int i) =>
        reader.IsDBNull(i) ? string.Empty : reader.GetValue(i).ToString() ?? string.Empty;
}
