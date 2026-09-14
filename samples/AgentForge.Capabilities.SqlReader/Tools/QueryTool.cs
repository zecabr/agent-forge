using System.Text;
using System.Text.Json.Nodes;
using AgentForge.Mcp.Server;
using Microsoft.Data.Sqlite;

namespace AgentForge.Capabilities.SqlReader.Tools;

internal sealed class QueryTool : McpTool
{
    private const int MaxRowsReturned = 100;
    private const int CommandTimeoutSeconds = 5;

    private readonly SqliteRoot _root;

    public QueryTool(SqliteRoot root)
    {
        _root = root;
    }

    public override string Name => "query";

    public override string Description =>
        "Runs a SELECT (or WITH ... SELECT) query against the SQLite database and returns rows as TSV "
        + "(header line + data rows). Read-only — INSERT/UPDATE/DELETE/DDL are rejected by the engine. "
        + "Capped at 100 rows and 5-second execution timeout. "
        + "Use list_tables and describe_table first to know the schema.";

    public override JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["sql"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "SQL SELECT statement to execute. Placeholders not supported — inline values with care.",
            },
        },
        ["required"] = new JsonArray { "sql" },
    };

    public override async Task<string> InvokeAsync(JsonNode? arguments, CancellationToken ct)
    {
        var sql = arguments?["sql"]?.GetValue<string>()
            ?? throw new ArgumentException("Missing required 'sql' argument.");

        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new ArgumentException("SQL must not be empty.");
        }

        await using var conn = await _root.OpenAsync(ct).ConfigureAwait(false);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = CommandTimeoutSeconds;

        SqliteDataReader reader;
        try
        {
            reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        }
        catch (SqliteException ex)
        {
            // Engine já rejeita writes por Mode=ReadOnly + PRAGMA query_only.
            // A exception vira isError:true (McpStdioServer wrappa) — mensagem clara pro LLM decidir.
            throw new InvalidOperationException($"SQLite error: {ex.Message}", ex);
        }

        await using (reader)
        {
            var sb = new StringBuilder();
            var columnCount = reader.FieldCount;

            for (var i = 0; i < columnCount; i++)
            {
                if (i > 0)
                {
                    sb.Append('\t');
                }

                sb.Append(reader.GetName(i));
            }

            sb.Append('\n');

            var rowCount = 0;
            var truncated = false;
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                if (rowCount >= MaxRowsReturned)
                {
                    truncated = true;
                    break;
                }

                for (var i = 0; i < columnCount; i++)
                {
                    if (i > 0)
                    {
                        sb.Append('\t');
                    }

                    sb.Append(FormatCell(reader, i));
                }

                sb.Append('\n');
                rowCount++;
            }

            if (rowCount == 0)
            {
                return sb.Append("(0 rows)").ToString();
            }

            if (truncated)
            {
                sb.Append("... (results truncated at ").Append(MaxRowsReturned).Append(" rows — add LIMIT to your query)");
            }
            else
            {
                sb.Append('(').Append(rowCount).Append(rowCount == 1 ? " row)" : " rows)");
            }

            return sb.ToString();
        }
    }

    private static string FormatCell(SqliteDataReader reader, int i)
    {
        if (reader.IsDBNull(i))
        {
            return "NULL";
        }

        var value = reader.GetValue(i)?.ToString() ?? string.Empty;
        // TSV: escape tab e newline pra não quebrar o formato de linha
        return value
            .Replace("\t", "\\t", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal);
    }
}
