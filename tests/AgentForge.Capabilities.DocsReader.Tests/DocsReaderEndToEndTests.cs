using AgentForge.Mcp;
using Xunit;

namespace AgentForge.Capabilities.DocsReader.Tests;

/// <summary>
/// Smoke test end-to-end: spawna o processo real <c>agent-forge-docs-reader</c>
/// via <see cref="McpHost"/> + <c>StdioTransport</c>, faz o handshake MCP de verdade
/// e valida cada uma das 3 tools da capability.
///
/// Prova que servidor (McpStdioServer) e cliente (McpHost) conversam em prod —
/// os testes de unidade cobrem cada lado com fakes, este é o único que junta os dois.
/// </summary>
public class DocsReaderEndToEndTests : IAsyncLifetime, IDisposable
{
    private string _tempRoot = string.Empty;
    private McpHost? _host;

    // Timeout defensivo — se algo travar (dead process, handshake não completa),
    // o teste falha em 10s em vez de pendurar o CI.
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(10);

    public Task InitializeAsync()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "docs-reader-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        Directory.CreateDirectory(Path.Combine(_tempRoot, "adr"));
        File.WriteAllText(Path.Combine(_tempRoot, "README.md"),
            "# End-to-end test root\n\nSample repo pra validar o docs-reader.");
        File.WriteAllText(Path.Combine(_tempRoot, "adr", "001.md"),
            "# ADR-001\n\nDecidimos usar MCP stdio JSON-RPC direto.");
        File.WriteAllText(Path.Combine(_tempRoot, "adr", "002.md"),
            "# ADR-002\n\nEval stack: xUnit-based LLM-as-judge.");
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
            _host = null;
        }

        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch { /* best-effort */ }
    }

    void IDisposable.Dispose() { /* IAsyncLifetime is the real cleanup path */ }

    private McpHost SpawnHost()
    {
        // O test project referencia o capability project, então o dll compilado
        // do docs-reader é copiado pro bin dos testes. Assembly.Location aponta
        // pra esse dll — passamos ele como argumento pro `dotnet`.
        var capabilityDll = typeof(DocsRoot).Assembly.Location;

        var config = new McpServerConfig(
            Name: "docs-reader",
            Command: "dotnet",
            Args: new[] { capabilityDll, "--root", _tempRoot });

        _host = new McpHost([config]);
        return _host;
    }

    [Fact]
    public async Task Discovers_The_Three_Docs_Reader_Tools()
    {
        using var cts = new CancellationTokenSource(CallTimeout);
        var host = SpawnHost();

        var tools = await host.DiscoverToolsAsync(cts.Token);

        var names = tools.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "list_docs", "read_doc", "search_docs" }, names);
    }

    [Fact]
    public async Task List_Docs_Returns_The_Three_Sample_Files()
    {
        using var cts = new CancellationTokenSource(CallTimeout);
        var host = SpawnHost();

        _ = await host.DiscoverToolsAsync(cts.Token);
        var result = await host.InvokeAsync(
            toolUseId: "call-1",
            toolName: "list_docs",
            argumentsJson: "{}",
            ct: cts.Token);

        Assert.False(result.IsError, $"Expected success, got error: {result.ResultJson}");
        var lines = result.ResultJson.Split('\n');
        Assert.Contains("README.md", lines);
        Assert.Contains("adr/001.md", lines);
        Assert.Contains("adr/002.md", lines);
    }

    [Fact]
    public async Task Search_Docs_Finds_Match_And_Read_Doc_Returns_Content()
    {
        using var cts = new CancellationTokenSource(CallTimeout);
        var host = SpawnHost();

        _ = await host.DiscoverToolsAsync(cts.Token);

        var searchResult = await host.InvokeAsync(
            toolUseId: "call-2",
            toolName: "search_docs",
            argumentsJson: """{"query": "LLM-as-judge"}""",
            ct: cts.Token);

        Assert.False(searchResult.IsError, $"search error: {searchResult.ResultJson}");
        Assert.Contains("adr/002.md", searchResult.ResultJson, StringComparison.Ordinal);

        var readResult = await host.InvokeAsync(
            toolUseId: "call-3",
            toolName: "read_doc",
            argumentsJson: """{"path": "adr/001.md"}""",
            ct: cts.Token);

        Assert.False(readResult.IsError, $"read error: {readResult.ResultJson}");
        Assert.Contains("ADR-001", readResult.ResultJson, StringComparison.Ordinal);
        Assert.Contains("MCP stdio JSON-RPC", readResult.ResultJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_Tool_Returns_Error_Without_Crashing_Session()
    {
        using var cts = new CancellationTokenSource(CallTimeout);
        var host = SpawnHost();

        _ = await host.DiscoverToolsAsync(cts.Token);

        var result = await host.InvokeAsync(
            toolUseId: "call-4",
            toolName: "does_not_exist",
            argumentsJson: "{}",
            ct: cts.Token);

        Assert.True(result.IsError);
        Assert.Contains("not found", result.ResultJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tool_Exception_On_Server_Is_Delivered_As_IsError_Result()
    {
        // path traversal — DocsRoot.ResolveSafely joga ArgumentException,
        // o McpStdioServer converte em isError:true (não JSON-RPC error).
        using var cts = new CancellationTokenSource(CallTimeout);
        var host = SpawnHost();

        _ = await host.DiscoverToolsAsync(cts.Token);

        var result = await host.InvokeAsync(
            toolUseId: "call-5",
            toolName: "read_doc",
            argumentsJson: """{"path": "../escape.md"}""",
            ct: cts.Token);

        Assert.True(result.IsError);
        Assert.Contains("outside docs root", result.ResultJson, StringComparison.Ordinal);
    }
}
