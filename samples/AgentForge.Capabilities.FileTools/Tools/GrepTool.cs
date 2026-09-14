using System.Text;
using System.Text.Json.Nodes;
using AgentForge.Mcp.Server;

namespace AgentForge.Capabilities.FileTools.Tools;

internal sealed class GrepTool : McpTool
{
    private const int MaxMatchesReturned = 50;
    private const int ContextLinesBefore = 1;
    private const int ContextLinesAfter = 1;

    private readonly FilesRoot _root;

    public GrepTool(FilesRoot root)
    {
        _root = root;
    }

    public override string Name => "grep";

    public override string Description =>
        "Searches for a term across all text files under the root. Case-insensitive substring match. "
        + "Optional 'pattern' filters which files are searched (default '*' = everything). "
        + "Skips common build/VCS directories: .git, .hg, .svn, bin, obj, node_modules, .vs, .vscode, .idea. "
        + "Returns up to 50 matches, each with file path, line number, and one line of context "
        + "above and below. Binary files are skipped. Files > 200 KB are skipped.";

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
            ["pattern"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Filesystem glob to filter files scanned (e.g. '*.cs'). Default '*'.",
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

        var pattern = arguments?["pattern"]?.GetValue<string>();

        var sb = new StringBuilder();
        var totalMatches = 0;
        var truncated = false;
        var skippedBinary = 0;
        var skippedTooBig = 0;

        foreach (var relativePath in _root.EnumerateFiles(pattern))
        {
            ct.ThrowIfCancellationRequested();

            var absolute = _root.ResolveSafely(relativePath);

            var info = new FileInfo(absolute);
            if (info.Length > FilesRoot.MaxBytesPerFile)
            {
                skippedTooBig++;
                continue;
            }

            var bytes = await File.ReadAllBytesAsync(absolute, ct).ConfigureAwait(false);
            if (FilesRoot.LooksBinary(bytes))
            {
                skippedBinary++;
                continue;
            }

            var text = Encoding.UTF8.GetString(bytes);
            var lines = text.Split('\n');

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
            var note = BuildSkipNote(skippedBinary, skippedTooBig);
            return note is null
                ? $"(no matches for '{query}')"
                : $"(no matches for '{query}'; {note})";
        }

        if (truncated)
        {
            sb.Append("\n... (results truncated at ").Append(MaxMatchesReturned).Append(" matches)");
        }

        var skipNote = BuildSkipNote(skippedBinary, skippedTooBig);
        if (skipNote is not null)
        {
            sb.Append("\n(skipped: ").Append(skipNote).Append(')');
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
            sb.Append(marker).Append(lines[j].TrimEnd('\r')).Append('\n');
        }

        sb.Append('\n');
    }

    private static string? BuildSkipNote(int binary, int tooBig)
    {
        if (binary == 0 && tooBig == 0)
        {
            return null;
        }

        var parts = new List<string>(2);
        if (binary > 0)
        {
            parts.Add($"{binary} binary");
        }

        if (tooBig > 0)
        {
            parts.Add($"{tooBig} > 200 KB");
        }

        return string.Join(", ", parts);
    }
}
