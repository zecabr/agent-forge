using System.Text.Json.Nodes;
using AgentForge.Capabilities.FileTools;
using AgentForge.Capabilities.FileTools.Tools;
using Xunit;

namespace AgentForge.Capabilities.FileTools.Tests;

public class ToolsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FilesRoot _root;

    public ToolsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "file-tools-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, "src"));
        File.WriteAllText(Path.Combine(_tempDir, "README.md"), "# Root\n\nOverview do file-tools.");
        File.WriteAllText(Path.Combine(_tempDir, "src", "foo.cs"),
            "public class Foo\n{\n    public string Bar => \"hello IChatProvider\";\n}\n");
        File.WriteAllText(Path.Combine(_tempDir, "src", "bar.cs"),
            "public class Bar\n{\n    // sem hits aqui\n}\n");
        File.WriteAllBytes(Path.Combine(_tempDir, "logo.bin"),
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x00, 0xFF, 0xFF, 0xFF });
        _root = new FilesRoot(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public async Task ListFiles_Without_Args_Returns_All()
    {
        var tool = new ListFilesTool(_root);
        var text = await tool.InvokeAsync(arguments: null, CancellationToken.None);
        var lines = text.Split('\n');

        Assert.Contains("README.md", lines);
        Assert.Contains("logo.bin", lines);
        Assert.Contains("src/foo.cs", lines);
        Assert.Contains("src/bar.cs", lines);
        Assert.Equal(4, lines.Length);
    }

    [Fact]
    public async Task ListFiles_With_Pattern_Filters_By_Extension()
    {
        var tool = new ListFilesTool(_root);
        var args = new JsonObject { ["pattern"] = "*.cs" };
        var text = await tool.InvokeAsync(args, CancellationToken.None);
        var lines = text.Split('\n');

        Assert.Contains("src/foo.cs", lines);
        Assert.Contains("src/bar.cs", lines);
        Assert.DoesNotContain("README.md", lines);
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public async Task ListFiles_With_Prefix_Filters_By_Directory()
    {
        var tool = new ListFilesTool(_root);
        var args = new JsonObject { ["prefix"] = "src/" };
        var text = await tool.InvokeAsync(args, CancellationToken.None);
        var lines = text.Split('\n');

        Assert.Equal(2, lines.Length);
        Assert.All(lines, l => Assert.StartsWith("src/", l));
    }

    [Fact]
    public async Task ListFiles_Non_Matching_Returns_Empty_Message()
    {
        var tool = new ListFilesTool(_root);
        var args = new JsonObject { ["pattern"] = "*.xyz" };
        var text = await tool.InvokeAsync(args, CancellationToken.None);

        Assert.Contains("no files matched", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadFile_Returns_Full_Content()
    {
        var tool = new ReadFileTool(_root);
        var args = new JsonObject { ["path"] = "src/foo.cs" };
        var text = await tool.InvokeAsync(args, CancellationToken.None);

        Assert.Contains("public class Foo", text, StringComparison.Ordinal);
        Assert.Contains("IChatProvider", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadFile_Rejects_Missing_Path()
    {
        var tool = new ReadFileTool(_root);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            tool.InvokeAsync(new JsonObject(), CancellationToken.None));
    }

    [Fact]
    public async Task ReadFile_Rejects_Nonexistent()
    {
        var tool = new ReadFileTool(_root);
        var args = new JsonObject { ["path"] = "does-not-exist.cs" };
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            tool.InvokeAsync(args, CancellationToken.None));
    }

    [Fact]
    public async Task ReadFile_Rejects_Path_Traversal()
    {
        var tool = new ReadFileTool(_root);
        var args = new JsonObject { ["path"] = "../escape.txt" };
        await Assert.ThrowsAsync<ArgumentException>(() =>
            tool.InvokeAsync(args, CancellationToken.None));
    }

    [Fact]
    public async Task ReadFile_Rejects_Binary_File()
    {
        var tool = new ReadFileTool(_root);
        var args = new JsonObject { ["path"] = "logo.bin" };
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tool.InvokeAsync(args, CancellationToken.None));
        Assert.Contains("binary", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Grep_Finds_Match_With_Context()
    {
        var tool = new GrepTool(_root);
        var args = new JsonObject { ["query"] = "IChatProvider" };
        var text = await tool.InvokeAsync(args, CancellationToken.None);

        Assert.Contains("src/foo.cs:", text, StringComparison.Ordinal);
        Assert.Contains("> ", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Grep_Is_Case_Insensitive()
    {
        var tool = new GrepTool(_root);
        var args = new JsonObject { ["query"] = "ICHATPROVIDER" };
        var text = await tool.InvokeAsync(args, CancellationToken.None);

        Assert.Contains("src/foo.cs", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Grep_Filter_By_Pattern_Narrows_Files()
    {
        var tool = new GrepTool(_root);
        var args = new JsonObject
        {
            ["query"] = "Overview",
            ["pattern"] = "*.cs",
        };
        var text = await tool.InvokeAsync(args, CancellationToken.None);

        // "Overview" só está no README, mas pattern *.cs exclui — resultado sem match
        Assert.Contains("no matches", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Grep_Skips_Binary_Files_And_Reports()
    {
        var tool = new GrepTool(_root);
        var args = new JsonObject { ["query"] = "PNG" };
        var text = await tool.InvokeAsync(args, CancellationToken.None);

        // logo.bin contém a string PNG mas é binário — pulou
        Assert.Contains("no matches", text, StringComparison.Ordinal);
        Assert.Contains("1 binary", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Grep_Rejects_Empty_Query()
    {
        var tool = new GrepTool(_root);
        var args = new JsonObject { ["query"] = "   " };
        await Assert.ThrowsAsync<ArgumentException>(() =>
            tool.InvokeAsync(args, CancellationToken.None));
    }

    [Fact]
    public async Task Grep_Rejects_Missing_Query()
    {
        var tool = new GrepTool(_root);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            tool.InvokeAsync(new JsonObject(), CancellationToken.None));
    }
}
