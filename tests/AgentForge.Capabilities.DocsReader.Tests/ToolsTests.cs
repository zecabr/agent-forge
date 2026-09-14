using System.Text.Json.Nodes;
using AgentForge.Capabilities.DocsReader;
using AgentForge.Capabilities.DocsReader.Tools;
using Xunit;

namespace AgentForge.Capabilities.DocsReader.Tests;

public class ToolsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly DocsRoot _root;

    public ToolsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "tools-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, "adr"));
        File.WriteAllText(Path.Combine(_tempDir, "README.md"), "# Root\n\nOverview do projeto agent-forge.");
        File.WriteAllText(Path.Combine(_tempDir, "adr", "001.md"),
            "# ADR-001\n\nMCP em .NET: custom stdio no v0.1.\nSubstituto do SDK oficial.\n");
        File.WriteAllText(Path.Combine(_tempDir, "adr", "002.md"),
            "# ADR-002\n\nEval stack: LLM-as-judge caseiro sobre xUnit.\n");
        _root = new DocsRoot(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch { /* best-effort */ }
    }

    [Fact]
    public async Task ListDocs_Without_Prefix_Returns_All_Markdown()
    {
        var tool = new ListDocsTool(_root);
        var text = await tool.InvokeAsync(arguments: null, CancellationToken.None);
        var lines = text.Split('\n');

        Assert.Contains("README.md", lines);
        Assert.Contains("adr/001.md", lines);
        Assert.Contains("adr/002.md", lines);
        Assert.Equal(3, lines.Length);
    }

    [Fact]
    public async Task ListDocs_With_Prefix_Filters_Results()
    {
        var tool = new ListDocsTool(_root);
        var args = new JsonObject { ["prefix"] = "adr/" };
        var text = await tool.InvokeAsync(args, CancellationToken.None);
        var lines = text.Split('\n');

        Assert.Contains("adr/001.md", lines);
        Assert.Contains("adr/002.md", lines);
        Assert.DoesNotContain("README.md", lines);
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public async Task ListDocs_With_NonMatching_Prefix_Returns_Empty_Message()
    {
        var tool = new ListDocsTool(_root);
        var args = new JsonObject { ["prefix"] = "runbook/" };
        var text = await tool.InvokeAsync(args, CancellationToken.None);

        Assert.Contains("no Markdown files matched", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadDoc_Returns_Full_Content()
    {
        var tool = new ReadDocTool(_root);
        var args = new JsonObject { ["path"] = "adr/001.md" };
        var text = await tool.InvokeAsync(args, CancellationToken.None);

        Assert.Contains("ADR-001", text, StringComparison.Ordinal);
        Assert.Contains("custom stdio", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadDoc_Rejects_Missing_Path_Argument()
    {
        var tool = new ReadDocTool(_root);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            tool.InvokeAsync(new JsonObject(), CancellationToken.None));
    }

    [Fact]
    public async Task ReadDoc_Rejects_Nonexistent_File()
    {
        var tool = new ReadDocTool(_root);
        var args = new JsonObject { ["path"] = "does-not-exist.md" };

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            tool.InvokeAsync(args, CancellationToken.None));
    }

    [Fact]
    public async Task ReadDoc_Rejects_Path_Traversal()
    {
        var tool = new ReadDocTool(_root);
        var args = new JsonObject { ["path"] = "../escape.md" };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            tool.InvokeAsync(args, CancellationToken.None));
    }

    [Fact]
    public async Task SearchDocs_Finds_Match_With_Context()
    {
        var tool = new SearchDocsTool(_root);
        var args = new JsonObject { ["query"] = "custom stdio" };
        var text = await tool.InvokeAsync(args, CancellationToken.None);

        Assert.Contains("adr/001.md:", text, StringComparison.Ordinal);
        Assert.Contains("> ", text, StringComparison.Ordinal); // marker da linha match
    }

    [Fact]
    public async Task SearchDocs_Is_Case_Insensitive()
    {
        var tool = new SearchDocsTool(_root);
        var args = new JsonObject { ["query"] = "CUSTOM STDIO" };
        var text = await tool.InvokeAsync(args, CancellationToken.None);

        Assert.Contains("adr/001.md", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchDocs_No_Match_Returns_Empty_Message()
    {
        var tool = new SearchDocsTool(_root);
        var args = new JsonObject { ["query"] = "xpto-nada-existe" };
        var text = await tool.InvokeAsync(args, CancellationToken.None);

        Assert.Contains("no matches", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchDocs_Rejects_Empty_Query()
    {
        var tool = new SearchDocsTool(_root);
        var args = new JsonObject { ["query"] = "   " };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            tool.InvokeAsync(args, CancellationToken.None));
    }

    [Fact]
    public async Task SearchDocs_Rejects_Missing_Query()
    {
        var tool = new SearchDocsTool(_root);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            tool.InvokeAsync(new JsonObject(), CancellationToken.None));
    }
}
