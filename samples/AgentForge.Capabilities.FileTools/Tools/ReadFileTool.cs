using System.Text;
using System.Text.Json.Nodes;
using AgentForge.Mcp.Server;

namespace AgentForge.Capabilities.FileTools.Tools;

internal sealed class ReadFileTool : McpTool
{
    private readonly FilesRoot _root;

    public ReadFileTool(FilesRoot root)
    {
        _root = root;
    }

    public override string Name => "read_file";

    public override string Description =>
        "Reads a text file under the root by its RELATIVE path (e.g. 'src/Foo.cs'). "
        + "Returns the full contents as UTF-8 text. Rejects paths outside the root, "
        + "files larger than 200 KB, and files detected as binary.";

    public override JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["path"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Relative path to the file (as returned by list_files).",
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
                $"File not found: {path}. Use list_files to see what exists.");
        }

        var info = new FileInfo(absolute);
        if (info.Length > FilesRoot.MaxBytesPerFile)
        {
            throw new InvalidOperationException(
                $"File '{path}' is {info.Length / 1024} KB — too large (limit {FilesRoot.MaxBytesPerFile / 1024} KB). "
                + "Try grep to locate the relevant section instead.");
        }

        var bytes = await File.ReadAllBytesAsync(absolute, ct).ConfigureAwait(false);
        if (FilesRoot.LooksBinary(bytes))
        {
            throw new InvalidOperationException(
                $"File '{path}' looks binary (contains NUL bytes in head). read_file only handles text.");
        }

        return Encoding.UTF8.GetString(bytes);
    }
}
