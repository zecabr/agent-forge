using AgentForge.Capabilities.SqlReader;
using AgentForge.Capabilities.SqlReader.Tools;
using AgentForge.Mcp.Server;

// Servidor MCP que expõe leitura SQL read-only contra um SQLite local via stdio.
// Uso: dotnet run --project samples/AgentForge.Capabilities.SqlReader -- --db <path>.db
//
// --db é obrigatório — sem um banco não há o que expor.
//
// Não escreve nada no stdout senão respostas JSON-RPC — logs vão pro stderr.

var dbPath = ParseDb(args);
if (dbPath is null)
{
    Console.Error.WriteLine("[sql-reader] error: --db <path> is required.");
    Console.Error.WriteLine("  example: dotnet run --project ... -- --db ./data/app.db");
    return 1;
}

SqliteRoot root;
try
{
    root = new SqliteRoot(dbPath);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[sql-reader] failed to open db '{dbPath}': {ex.Message}");
    return 1;
}

Console.Error.WriteLine($"[sql-reader] serving MCP over stdio, db: {root.DbPath} (read-only)");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var tools = new McpTool[]
{
    new ListTablesTool(root),
    new DescribeTableTool(root),
    new QueryTool(root),
};

try
{
    await McpStdioServer.RunAsync(
        serverName: "sql-reader",
        serverVersion: "0.1.0",
        tools: tools,
        ct: cts.Token);
}
catch (OperationCanceledException)
{
    // shutdown normal
}

Console.Error.WriteLine("[sql-reader] shutting down");
return 0;

static string? ParseDb(string[] args)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == "--db")
        {
            return args[i + 1];
        }
    }

    return null;
}
