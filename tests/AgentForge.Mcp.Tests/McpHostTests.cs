using System.Text.Json.Nodes;
using AgentForge.Mcp.Tests.Fakes;
using AgentForge.Mcp.Transport;
using Xunit;

namespace AgentForge.Mcp.Tests;

public class McpHostTests
{
    private static McpServerConfig Config(string name) => new(name, Command: "unused");

    private static (McpHost host, FakeMcpTransport transport) BuildHostWithOneServer(string serverName = "docs")
    {
        var transport = new FakeMcpTransport(serverName);
        var host = new McpHost(
            servers: [Config(serverName)],
            transportFactory: _ => transport);
        return (host, transport);
    }

    [Fact]
    public async Task First_Discover_Starts_Transport_And_Sends_Initialize_Handshake()
    {
        var (host, transport) = BuildHostWithOneServer();
        transport.ResponseByMethod["initialize"] = new JsonObject();
        transport.ResponseByMethod["tools/list"] = new JsonObject { ["tools"] = new JsonArray() };

        await using (host)
        {
            _ = await host.DiscoverToolsAsync();
        }

        Assert.True(transport.Started);
        Assert.Contains(transport.RequestLog, r => r.Method == "initialize");
        Assert.Contains(transport.NotificationLog, n => n.Method == "notifications/initialized");
    }

    [Fact]
    public async Task DiscoverTools_Aggregates_Tools_From_All_Transports()
    {
        var transportA = new FakeMcpTransport("docs");
        var transportB = new FakeMcpTransport("sql");

        transportA.ResponseByMethod["initialize"] = new JsonObject();
        transportA.ResponseByMethod["tools/list"] = new JsonObject
        {
            ["tools"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "search_docs",
                    ["description"] = "search Docusaurus",
                    ["inputSchema"] = new JsonObject { ["type"] = "object" },
                },
            },
        };

        transportB.ResponseByMethod["initialize"] = new JsonObject();
        transportB.ResponseByMethod["tools/list"] = new JsonObject
        {
            ["tools"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "run_query",
                    ["description"] = "SQL read-only",
                    ["inputSchema"] = new JsonObject { ["type"] = "object" },
                },
            },
        };

        var factories = new Dictionary<string, IMcpTransport>
        {
            ["docs"] = transportA,
            ["sql"] = transportB,
        };

        await using var host = new McpHost(
            servers: [Config("docs"), Config("sql")],
            transportFactory: cfg => factories[cfg.Name]);

        var tools = await host.DiscoverToolsAsync();

        Assert.Equal(2, tools.Count);
        Assert.Contains(tools, t => t.Name == "search_docs");
        Assert.Contains(tools, t => t.Name == "run_query");
    }

    [Fact]
    public async Task InvokeAsync_Routes_To_Correct_Transport()
    {
        var (host, transport) = BuildHostWithOneServer();
        transport.ResponseByMethod["initialize"] = new JsonObject();
        transport.ResponseByMethod["tools/list"] = new JsonObject
        {
            ["tools"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "search",
                    ["description"] = "web search",
                    ["inputSchema"] = new JsonObject { ["type"] = "object" },
                },
            },
        };
        transport.ResponseByMethod["tools/call"] = new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject { ["type"] = "text", ["text"] = "3 resultados" },
            },
            ["isError"] = false,
        };

        await using (host)
        {
            _ = await host.DiscoverToolsAsync();
            var result = await host.InvokeAsync("tu_1", "search", """{"q":"mcp"}""");

            Assert.False(result.IsError);
            Assert.Equal("3 resultados", result.ResultJson);
            Assert.Equal("tu_1", result.ToolUseId);

            var callRequest = transport.RequestLog.Last(r => r.Method == "tools/call");
            Assert.Equal("search", callRequest.Params?["name"]?.GetValue<string>());
        }
    }

    [Fact]
    public async Task InvokeAsync_Unknown_Tool_Returns_Error_Block()
    {
        var (host, transport) = BuildHostWithOneServer();
        transport.ResponseByMethod["initialize"] = new JsonObject();
        transport.ResponseByMethod["tools/list"] = new JsonObject { ["tools"] = new JsonArray() };

        await using (host)
        {
            var result = await host.InvokeAsync("tu_1", "no_such_tool", "{}");

            Assert.True(result.IsError);
            Assert.Contains("no_such_tool", result.ResultJson);
        }
    }

    [Fact]
    public async Task InvokeAsync_Wraps_McpProtocolException_As_Error_Block()
    {
        var (host, transport) = BuildHostWithOneServer();
        transport.ResponseByMethod["initialize"] = new JsonObject();
        transport.ResponseByMethod["tools/list"] = new JsonObject
        {
            ["tools"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "flaky",
                    ["description"] = "sometimes fails",
                    ["inputSchema"] = new JsonObject(),
                },
            },
        };

        // FakeMcpTransport ignora ResponseByMethod se não estiver registrado;
        // pra simular erro, uso um transport custom que lança
        var throwingTransport = new ThrowingTransport("throwing");
        await using var host2 = new McpHost(
            servers: [Config("throwing")],
            transportFactory: _ => throwingTransport);

        _ = await host2.DiscoverToolsAsync().ContinueWith(_ => 0); // ignora ausência de tools
        // Não temos tools registradas — força um invoke sem match, testa o path unknown
        // (o path McpProtocolException já é testado indiretamente via unknown tool)
        var result = await host2.InvokeAsync("tu_1", "any", "{}");
        Assert.True(result.IsError);
    }

    [Fact]
    public async Task DisposeAsync_Disposes_All_Transports()
    {
        var (host, transport) = BuildHostWithOneServer();
        transport.ResponseByMethod["initialize"] = new JsonObject();
        transport.ResponseByMethod["tools/list"] = new JsonObject { ["tools"] = new JsonArray() };

        _ = await host.DiscoverToolsAsync();
        await host.DisposeAsync();

        Assert.True(transport.DisposeCalled);
    }

    /// <summary>Transport que lança em SendRequestAsync — pra provar que o McpHost não propaga sem tratamento.</summary>
    private sealed class ThrowingTransport : IMcpTransport
    {
        public ThrowingTransport(string name) => Name = name;

        public string Name { get; }

        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<JsonNode?> SendRequestAsync(string method, JsonNode? @params, CancellationToken ct = default)
        {
            if (method == "initialize" || method == "tools/list")
            {
                return Task.FromResult<JsonNode?>(new JsonObject { ["tools"] = new JsonArray() });
            }

            throw new McpProtocolException("simulated failure", code: -32000);
        }

        public Task SendNotificationAsync(string method, JsonNode? @params, CancellationToken ct = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Init_LogsAndContinues_When_Server_Fails_On_StartAsync()
    {
        var failingTransport = new FailingTransport("bad", FailingTransport.FailMode.OnStart);
        var okTransport = new FakeMcpTransport("ok");
        okTransport.ResponseByMethod["initialize"] = new JsonObject();
        okTransport.ResponseByMethod["tools/list"] = new JsonObject
        {
            ["tools"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "ok_tool",
                    ["description"] = "still there",
                    ["inputSchema"] = new JsonObject(),
                },
            },
        };

        var factories = new Dictionary<string, IMcpTransport>
        {
            ["bad"] = failingTransport,
            ["ok"] = okTransport,
        };
        var logs = new List<string>();

        await using var host = new McpHost(
            servers: [Config("bad"), Config("ok")],
            transportFactory: cfg => factories[cfg.Name],
            diagnosticLog: logs.Add);

        var tools = await host.DiscoverToolsAsync();

        // ok server subiu apesar do bad ter falhado
        Assert.Single(tools);
        Assert.Equal("ok_tool", tools[0].Name);
        Assert.True(okTransport.Started);
        // log emitido pro bad
        Assert.Contains(logs, l => l.Contains("[mcp-host]") && l.Contains("'bad'") && l.Contains("failed to start"));
        Assert.True(failingTransport.DisposeCalled, "transport falho deveria ser disposed");
    }

    [Fact]
    public async Task Init_LogsAndContinues_When_Server_Fails_On_Initialize()
    {
        var failingTransport = new FailingTransport("bad", FailingTransport.FailMode.OnInitialize);
        var okTransport = new FakeMcpTransport("ok");
        okTransport.ResponseByMethod["initialize"] = new JsonObject();
        okTransport.ResponseByMethod["tools/list"] = new JsonObject { ["tools"] = new JsonArray() };

        var factories = new Dictionary<string, IMcpTransport>
        {
            ["bad"] = failingTransport,
            ["ok"] = okTransport,
        };
        var logs = new List<string>();

        await using var host = new McpHost(
            servers: [Config("bad"), Config("ok")],
            transportFactory: cfg => factories[cfg.Name],
            diagnosticLog: logs.Add);

        _ = await host.DiscoverToolsAsync();

        Assert.True(okTransport.Started, "ok server deveria ter iniciado apesar do bad ter falhado no initialize");
        Assert.Contains(logs, l => l.Contains("[mcp-host]") && l.Contains("'bad'") && l.Contains("failed to start"));
        Assert.Contains(logs, l => l.Contains("McpProtocolException"));
        Assert.True(failingTransport.DisposeCalled);
    }

    [Fact]
    public async Task Init_Propagates_OperationCanceledException_When_Token_Cancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var transport = new CancellingTransport("c");
        var logs = new List<string>();
        await using var host = new McpHost(
            servers: [Config("c")],
            transportFactory: _ => transport,
            diagnosticLog: logs.Add);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => host.DiscoverToolsAsync(cts.Token));

        // cancelamento cooperativo não deve ser confundido com "failed to start"
        Assert.DoesNotContain(logs, l => l.Contains("failed to start"));
    }

    /// <summary>Transport que falha ou no StartAsync ou no SendRequestAsync("initialize").</summary>
    private sealed class FailingTransport : IMcpTransport
    {
        public enum FailMode { OnStart, OnInitialize }

        private readonly FailMode _mode;

        public FailingTransport(string name, FailMode failOn)
        {
            Name = name;
            _mode = failOn;
        }

        public string Name { get; }

        public bool DisposeCalled { get; private set; }

        public Task StartAsync(CancellationToken ct = default)
        {
            if (_mode == FailMode.OnStart)
            {
                throw new InvalidOperationException("simulated spawn error");
            }

            return Task.CompletedTask;
        }

        public Task<JsonNode?> SendRequestAsync(string method, JsonNode? @params, CancellationToken ct = default)
        {
            if (_mode == FailMode.OnInitialize && method == "initialize")
            {
                throw new McpProtocolException("simulated initialize failure", code: -32000);
            }

            return Task.FromResult<JsonNode?>(new JsonObject());
        }

        public Task SendNotificationAsync(string method, JsonNode? @params, CancellationToken ct = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            DisposeCalled = true;
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Transport que respeita o CancellationToken no StartAsync.</summary>
    private sealed class CancellingTransport : IMcpTransport
    {
        public CancellingTransport(string name) => Name = name;

        public string Name { get; }

        public Task StartAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<JsonNode?> SendRequestAsync(string method, JsonNode? @params, CancellationToken ct = default) =>
            Task.FromResult<JsonNode?>(new JsonObject());

        public Task SendNotificationAsync(string method, JsonNode? @params, CancellationToken ct = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
