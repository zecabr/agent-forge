using System.Text;
using System.Text.Json.Nodes;
using AgentForge.Mcp.Server;

namespace AgentForge.Capabilities.DocsReader.Tools;

internal sealed class SearchDocsTool : McpTool
{
    // Cap defensivo: uma busca que traz mais do que isso não é útil pro LLM.
    private const int MaxMatchesReturned = 30;

    // Contexto: 1 linha antes + linha match + 1 depois — suficiente pra decidir se vale ler o doc inteiro.
    private const int ContextLinesBefore = 1;
    private const int ContextLinesAfter = 1;

    private readonly DocsRoot _root;

    public SearchDocsTool(DocsRoot root)
    {
        _root = root;
    }

    public override string Name => "search_docs";

    public override string Description =>
        "Searches for a term across all Markdown files under the docs root. "
        + "Case-insensitive substring match. Returns up to 30 matches, each with the file path, "
        + "line number, and one line of context above and below. "
        + "Use this to locate the RIGHT doc before calling read_doc.";

    public override JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["query"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "The term to search for. Case-insensitive.",
            },
        },
        ["required"] = new JsonArray { "query" },
    };

    public override async Task<string> InvokeAsync(JsonNode? arguments, CancellationToken ct)
    {
        var query = arguments?["query"]?.GetValue<string>()
            ?? throw new ArgumentException("Missing required 'query' argument.");

        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Query must not be empty or whitespace.");
        }

        var sb = new StringBuilder();
        var totalMatches = 0;
        var truncated = false;

        foreach (var relativePath in _root.EnumerateMarkdown())
        {
            ct.ThrowIfCancellationRequested();

            var absolute = _root.ResolveSafely(relativePath);
            var lines = await File.ReadAllLinesAsync(absolute, ct).ConfigureAwait(false);

            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    if (totalMatches >= MaxMatchesReturned)
                    {
                        truncated = true;
                        break;
                    }

                    AppendMatch(sb, relativePath, lines, i);
                    totalMatches++;
                }
            }

            if (truncated)
            {
                break;
            }
        }

        if (totalMatches == 0)
        {
            return $"(no matches for '{query}')";
        }

        if (truncated)
        {
            sb.Append("\n... (results truncated at ").Append(MaxMatchesReturned).Append(" matches)");
        }

        return sb.ToString().TrimEnd('\n');
    }

    private static void AppendMatch(StringBuilder sb, string path, string[] lines, int matchIndex)
    {
        var start = Math.Max(0, matchIndex - ContextLinesBefore);
        var end = Math.Min(lines.Length - 1, matchIndex + ContextLinesAfter);

        sb.Append(path).Append(':').Append(matchIndex + 1).Append('\n');
        for (var j = start; j <= end; j++)
        {
            var marker = j == matchIndex ? "> " : "  ";
            sb.Append(marker).Append(lines[j]).Append('\n');
        }

        sb.Append('\n');
    }
}
