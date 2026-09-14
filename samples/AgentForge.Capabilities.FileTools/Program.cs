using AgentForge.Capabilities.FileTools;
using AgentForge.Capabilities.FileTools.Tools;
using AgentForge.Mcp.Server;

// Servidor MCP que expõe leitura genérica de arquivos texto sob um root via stdio.
// Uso: dotnet run --project samples/AgentForge.Capabilities.FileTools -- --root <path>
//
// Se --root não vier, usa o diretório atual como raiz.
//
// Não escreve nada no stdout senão respostas JSON-RPC — logs vão pro stderr.
// (Um servidor MCP mal-comportado que loga no stdout confunde o cliente.)

var rootPath = ParseRoot(args) ?? Environment.CurrentDirectory;

FilesRoot root;
try
{
    root = new FilesRoot(rootPath);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[file-tools] failed to open root '{rootPath}': {ex.Message}");
    return 1;
}

Console.Error.WriteLine($"[file-tools] serving MCP over stdio, root: {root.CanonicalPath}");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var tools = new McpTool[]
{
    new ListFilesTool(root),
    new GrepTool(root),
    new ReadFileTool(root),
};

try
{
    await McpStdioServer.RunAsync(
        serverName: "file-tools",
        serverVersion: "0.1.0",
        tools: tools,
        ct: cts.Token);
}
catch (OperationCanceledException)
{
    // shutdown normal
}

Console.Error.WriteLine("[file-tools] shutting down");
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
