using System.Text;
using System.Text.Json.Nodes;
using AgentForge.Mcp.Server;

namespace AgentForge.Capabilities.DocsReader.Tools;

internal sealed class ListDocsTool : McpTool
{
    private readonly DocsRoot _root;

    public ListDocsTool(DocsRoot root)
    {
        _root = root;
    }

    public override string Name => "list_docs";

    public override string Description =>
        "Lists all Markdown files (*.md) under the docs root, recursively. "
        + "Returns one relative path per line. Optional 'prefix' filters to paths starting with that prefix. "
        + "Use this to discover what documentation exists before deciding what to read or search.";

    public override JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["prefix"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Optional path prefix filter (e.g. 'adr/' returns only ADRs).",
            },
        },
    };

    public override Task<string> InvokeAsync(JsonNode? arguments, CancellationToken ct)
    {
        var prefix = arguments?["prefix"]?.GetValue<string>();

        var sb = new StringBuilder();
        var count = 0;
        foreach (var path in _root.EnumerateMarkdown())
        {
            ct.ThrowIfCancellationRequested();

            if (prefix is not null && !path.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            sb.Append(path).Append('\n');
            count++;
        }

        if (count == 0)
        {
            return Task.FromResult(prefix is null
                ? "(no Markdown files found under docs root)"
                : $"(no Markdown files matched prefix '{prefix}')");
        }

        return Task.FromResult(sb.ToString().TrimEnd('\n'));
    }
}
