using System.Text;
using System.Text.Json.Nodes;
using AgentForge.Mcp.Server;

namespace AgentForge.Capabilities.FileTools.Tools;

internal sealed class ListFilesTool : McpTool
{
    private const int MaxPathsReturned = 500;

    private readonly FilesRoot _root;

    public ListFilesTool(FilesRoot root)
    {
        _root = root;
    }

    public override string Name => "list_files";

    public override string Description =>
        "Lists files under the root directory, recursively. Returns one relative path per line. "
        + "Optional 'pattern' is a filesystem glob (default '*' = everything), e.g. '*.cs' or '*.md'. "
        + "Optional 'prefix' filters to paths starting with that string. "
        + "Capped at 500 paths — use pattern/prefix to narrow when the tree is large.";

    public override JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["pattern"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Filesystem glob (e.g. '*.cs', 'README*'). Default '*'.",
            },
            ["prefix"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Path prefix filter (e.g. 'src/' returns only files under src/).",
            },
        },
    };

    public override Task<string> InvokeAsync(JsonNode? arguments, CancellationToken ct)
    {
        var pattern = arguments?["pattern"]?.GetValue<string>();
        var prefix = arguments?["prefix"]?.GetValue<string>();

        var sb = new StringBuilder();
        var count = 0;
        var truncated = false;

        foreach (var path in _root.EnumerateFiles(pattern))
        {
            ct.ThrowIfCancellationRequested();

            if (prefix is not null && !path.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (count >= MaxPathsReturned)
            {
                truncated = true;
                break;
            }

            sb.Append(path).Append('\n');
            count++;
        }

        if (count == 0)
        {
            var hint = (pattern, prefix) switch
            {
                (null, null) => "(no files under files root)",
                (not null, null) => $"(no files matched pattern '{pattern}')",
                (null, not null) => $"(no files matched prefix '{prefix}')",
                _ => $"(no files matched pattern '{pattern}' with prefix '{prefix}')",
            };
            return Task.FromResult(hint);
        }

        if (truncated)
        {
            sb.Append("... (results truncated at ").Append(MaxPathsReturned).Append(" paths — narrow with pattern or prefix)");
        }

        return Task.FromResult(sb.ToString().TrimEnd('\n'));
    }
}
