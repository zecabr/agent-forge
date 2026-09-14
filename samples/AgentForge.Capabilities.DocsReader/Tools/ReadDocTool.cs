using System.Text.Json.Nodes;
using AgentForge.Mcp.Server;

namespace AgentForge.Capabilities.DocsReader.Tools;

internal sealed class ReadDocTool : McpTool
{
    // Cap defensivo: um arquivo MD razoável cabe em 200 KB. Acima disso
    // provavelmente é o alvo errado ou algo que precisa ser consumido em pedaços.
    private const int MaxBytes = 200 * 1024;

    private readonly DocsRoot _root;

    public ReadDocTool(DocsRoot root)
    {
        _root = root;
    }

    public override string Name => "read_doc";

    public override string Description =>
        "Reads a specific Markdown file under the docs root by its RELATIVE path "
        + "(e.g. 'adr/001-mcp-em-dotnet.md'). Returns the full contents as text. "
        + "Rejects paths outside the docs root and files larger than 200 KB.";

    public override JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["path"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Relative path to the .md file (as returned by list_docs).",
            },
        },
        ["required"] = new JsonArray { "path" },
    };

    public override async Task<string> InvokeAsync(JsonNode? arguments, CancellationToken ct)
    {
        var path = arguments?["path"]?.GetValue<string>()
            ?? throw new ArgumentException("Missing required 'path' argument.");

        var absolute = _root.ResolveSafely(path);

        if (!File.Exists(absolute))
        {
            throw new FileNotFoundException(
                $"Doc not found: {path}. Use list_docs to see what exists.");
        }

        var info = new FileInfo(absolute);
        if (info.Length > MaxBytes)
        {
            throw new InvalidOperationException(
                $"Doc '{path}' is {info.Length / 1024} KB — too large (limit {MaxBytes / 1024} KB). "
                + "Try search_docs to find the relevant section instead.");
        }

        return await File.ReadAllTextAsync(absolute, ct).ConfigureAwait(false);
    }
}
