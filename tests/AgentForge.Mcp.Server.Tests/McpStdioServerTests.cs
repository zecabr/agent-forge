using System.Text;
using System.Text.Json.Nodes;
using AgentForge.Mcp.Server;
using Xunit;

namespace AgentForge.Mcp.Server.Tests;

public class McpStdioServerTests
{
    private const string ServerName = "test-server";
    private const string ServerVersion = "1.2.3";

    [Fact]
    public async Task Handles_Initialize_And_Returns_Server_Info()
    {
        var responses = await RunAsync(
            tools: Array.Empty<McpTool>(),
            lines: new[]
            {
                Envelope(id: 1, method: "initialize", @params: new JsonObject { ["protocolVersion"] = "2024-11-05" }),
            });

        Assert.Single(responses);
        var result = responses[0]!["result"]!.AsObject();
        Assert.Equal("2024-11-05", result["protocolVersion"]!.GetValue<string>());
        Assert.Equal(ServerName, result["serverInfo"]!["name"]!.GetValue<string>());
        Assert.Equal(ServerVersion, result["serverInfo"]!["version"]!.GetValue<string>());
        Assert.NotNull(result["capabilities"]!["tools"]);
    }

    [Fact]
    public async Task Initialized_Notification_Does_Not_Respond()
    {
        var responses = await RunAsync(
            tools: Array.Empty<McpTool>(),
            lines: new[]
            {
                Envelope(id: null, method: "notifications/initialized", @params: null),
            });

        Assert.Empty(responses);
    }

    [Fact]
    public async Task Tools_List_Returns_Registered_Tools()
    {
        var tool = new EchoTool();
        var responses = await RunAsync(
            tools: new[] { (McpTool)tool },
            lines: new[]
            {
                Envelope(id: 2, method: "tools/list", @params: null),
            });

        var list = responses[0]!["result"]!["tools"]!.AsArray();
        Assert.Single(list);
        Assert.Equal("echo", list[0]!["name"]!.GetValue<string>());
        Assert.Equal("Echoes back the input string.", list[0]!["description"]!.GetValue<string>());
        Assert.NotNull(list[0]!["inputSchema"]);
    }

    [Fact]
    public async Task Tools_Call_Invokes_Tool_And_Returns_Text_Content()
    {
        var tool = new EchoTool();
        var responses = await RunAsync(
            tools: new[] { (McpTool)tool },
            lines: new[]
            {
                Envelope(id: 3, method: "tools/call", @params: new JsonObject
                {
                    ["name"] = "echo",
                    ["arguments"] = new JsonObject { ["text"] = "olá" },
                }),
            });

        var result = responses[0]!["result"]!;
        Assert.False(result["isError"]!.GetValue<bool>());
        var content = result["content"]!.AsArray()[0]!;
        Assert.Equal("text", content["type"]!.GetValue<string>());
        Assert.Equal("olá", content["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task Tools_Call_With_Unknown_Tool_Returns_MethodNotFound()
    {
        var responses = await RunAsync(
            tools: Array.Empty<McpTool>(),
            lines: new[]
            {
                Envelope(id: 4, method: "tools/call", @params: new JsonObject { ["name"] = "ghost" }),
            });

        var error = responses[0]!["error"]!;
        Assert.Equal(-32601, error["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task Tools_Call_Missing_Name_Returns_InvalidParams()
    {
        var responses = await RunAsync(
            tools: new[] { (McpTool)new EchoTool() },
            lines: new[]
            {
                Envelope(id: 5, method: "tools/call", @params: new JsonObject()),
            });

        Assert.Equal(-32602, responses[0]!["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task Tool_Exception_Becomes_IsError_Result_Not_JsonRpc_Error()
    {
        var responses = await RunAsync(
            tools: new[] { (McpTool)new ThrowingTool() },
            lines: new[]
            {
                Envelope(id: 6, method: "tools/call", @params: new JsonObject
                {
                    ["name"] = "boom",
                    ["arguments"] = new JsonObject(),
                }),
            });

        var envelope = responses[0]!.AsObject();
        Assert.Null(envelope["error"]);
        var result = envelope["result"]!;
        Assert.True(result["isError"]!.GetValue<bool>());
        var text = result["content"]!.AsArray()[0]!["text"]!.GetValue<string>();
        Assert.Contains("InvalidOperationException", text, StringComparison.Ordinal);
        Assert.Contains("something broke", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_Method_Returns_MethodNotFound()
    {
        var responses = await RunAsync(
            tools: Array.Empty<McpTool>(),
            lines: new[]
            {
                Envelope(id: 7, method: "resources/list", @params: null),
            });

        Assert.Equal(-32601, responses[0]!["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task Malformed_Json_Returns_ParseError()
    {
        var responses = await RunAsync(
            tools: Array.Empty<McpTool>(),
            rawLines: new[] { "{not json at all" });

        Assert.Equal(-32700, responses[0]!["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task Non_Object_Envelope_Returns_InvalidRequest()
    {
        var responses = await RunAsync(
            tools: Array.Empty<McpTool>(),
            rawLines: new[] { "[1,2,3]" });

        Assert.Equal(-32600, responses[0]!["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task Multiple_Sequential_Requests_Are_Handled_In_Order()
    {
        var tool = new EchoTool();
        var responses = await RunAsync(
            tools: new[] { (McpTool)tool },
            lines: new[]
            {
                Envelope(id: 10, method: "initialize", @params: null),
                Envelope(id: 11, method: "tools/list", @params: null),
                Envelope(id: 12, method: "tools/call", @params: new JsonObject
                {
                    ["name"] = "echo",
                    ["arguments"] = new JsonObject { ["text"] = "first" },
                }),
                Envelope(id: 13, method: "tools/call", @params: new JsonObject
                {
                    ["name"] = "echo",
                    ["arguments"] = new JsonObject { ["text"] = "second" },
                }),
            });

        Assert.Equal(4, responses.Count);
        Assert.Equal(10, responses[0]!["id"]!.GetValue<int>());
        Assert.Equal(11, responses[1]!["id"]!.GetValue<int>());
        Assert.Equal(12, responses[2]!["id"]!.GetValue<int>());
        Assert.Equal(13, responses[3]!["id"]!.GetValue<int>());
        Assert.Equal("first", responses[2]!["result"]!["content"]!.AsArray()[0]!["text"]!.GetValue<string>());
        Assert.Equal("second", responses[3]!["result"]!["content"]!.AsArray()[0]!["text"]!.GetValue<string>());
    }

    private static string Envelope(int? id, string method, JsonNode? @params)
    {
        var obj = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
        };
        if (id is not null)
        {
            obj["id"] = id.Value;
        }

        if (@params is not null)
        {
            obj["params"] = @params;
        }

        return obj.ToJsonString();
    }

    private static async Task<IReadOnlyList<JsonNode?>> RunAsync(
        IReadOnlyList<McpTool> tools,
        IReadOnlyList<string>? lines = null,
        IReadOnlyList<string>? rawLines = null)
    {
        var input = new StringBuilder();
        foreach (var l in lines ?? Array.Empty<string>())
        {
            input.AppendLine(l);
        }

        foreach (var l in rawLines ?? Array.Empty<string>())
        {
            input.AppendLine(l);
        }

        using var reader = new StringReader(input.ToString());
        using var writer = new StringWriter();

        await McpStdioServer.RunAsync(reader, writer, ServerName, ServerVersion, tools);

        var outputText = writer.ToString();
        return outputText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => (JsonNode?)JsonNode.Parse(line))
            .ToList();
    }

    private sealed class EchoTool : McpTool
    {
        public override string Name => "echo";
        public override string Description => "Echoes back the input string.";
        public override JsonObject InputSchema => new()
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["text"] = new JsonObject { ["type"] = "string" },
            },
            ["required"] = new JsonArray { "text" },
        };

        public override Task<string> InvokeAsync(JsonNode? arguments, CancellationToken ct)
        {
            var text = arguments?["text"]?.GetValue<string>() ?? string.Empty;
            return Task.FromResult(text);
        }
    }

    private sealed class ThrowingTool : McpTool
    {
        public override string Name => "boom";
        public override string Description => "Always throws.";
        public override JsonObject InputSchema => new() { ["type"] = "object" };

        public override Task<string> InvokeAsync(JsonNode? arguments, CancellationToken ct)
            => throw new InvalidOperationException("something broke");
    }
}
