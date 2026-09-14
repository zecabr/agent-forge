using AgentForge.Capabilities.DocsReader;
using AgentForge.Capabilities.DocsReader.Tools;
using AgentForge.Mcp.Server;

// Servidor MCP que expõe leitura de docs Markdown local via stdio.
// Uso: dotnet run --project samples/AgentForge.Capabilities.DocsReader -- --root <path>
//
// Se --root não vier, usa o diretório atual como raiz.
//
// Não escreve nada no stdout senão respostas JSON-RPC — logs vão pro stderr.
// (Um servidor MCP mal-comportado que loga no stdout confunde o cliente.)

var rootPath = ParseRoot(args) ?? Environment.CurrentDirectory;

DocsRoot root;
try
{
    root = new DocsRoot(rootPath);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[docs-reader] failed to open root '{rootPath}': {ex.Message}");
    return 1;
}

Console.Error.WriteLine($"[docs-reader] serving MCP over stdio, root: {root.CanonicalPath}");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var tools = new McpTool[]
{
    new ListDocsTool(root),
    new SearchDocsTool(root),
    new ReadDocTool(root),
};

try
{
    await McpStdioServer.RunAsync(
        serverName: "docs-reader",
        serverVersion: "0.1.0",
        tools: tools,
        ct: cts.Token);
}
catch (OperationCanceledException)
{
    // shutdown normal
}

Console.Error.WriteLine("[docs-reader] shutting down");
return 0;

static string? ParseRoot(string[] args)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == "--root")
        {
            return args[i + 1];
        }
    }

    return null;
}
