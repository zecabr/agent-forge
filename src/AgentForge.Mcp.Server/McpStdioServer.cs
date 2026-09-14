using System.Text.Json.Nodes;

namespace AgentForge.Mcp.Server;

/// <summary>
/// Loop de servidor MCP sobre stdio: lê JSON-RPC 2.0 line-framed do stdin,
/// escreve respostas no stdout. Implementa <c>initialize</c>, <c>tools/list</c>,
/// <c>tools/call</c> e a notification <c>notifications/initialized</c>.
/// <para>
/// Uso típico num <c>Program.cs</c> de capability:
/// </para>
/// <code>
/// await McpStdioServer.RunAsync(
///     serverName: "docs-reader",
///     serverVersion: "0.1.0",
///     tools: new McpTool[] { new SearchDocsTool(root), new ReadDocTool(root) });
/// </code>
/// </summary>
public static class McpStdioServer
{
    /// <summary>Versão do protocolo MCP que este servidor implementa.</summary>
    public const string ProtocolVersion = "2024-11-05";

    // JSON-RPC 2.0 error codes canônicos.
    private const int ErrorParseError = -32700;
    private const int ErrorInvalidRequest = -32600;
    private const int ErrorMethodNotFound = -32601;
    private const int ErrorInvalidParams = -32602;
    private const int ErrorInternal = -32603;

    /// <summary>
    /// Roda o loop de servidor até o stdin fechar ou o token cancelar.
    /// Usa <see cref="Console.OpenStandardInput"/> e <see cref="Console.OpenStandardOutput"/>.
    /// </summary>
    public static async Task RunAsync(
        string serverName,
        string serverVersion,
        IReadOnlyList<McpTool> tools,
        CancellationToken ct = default)
    {
        using var stdin = new StreamReader(Console.OpenStandardInput());
        using var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        await RunAsync(stdin, stdout, serverName, serverVersion, tools, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Overload testável: aceita <see cref="TextReader"/>/<see cref="TextWriter"/> injetados.
    /// </summary>
    public static async Task RunAsync(
        TextReader input,
        TextWriter output,
        string serverName,
        string serverVersion,
        IReadOnlyList<McpTool> tools,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverVersion);
        ArgumentNullException.ThrowIfNull(tools);

        var toolIndex = tools.ToDictionary(t => t.Name, StringComparer.Ordinal);

        string? line;
        while (!ct.IsCancellationRequested && (line = await input.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
        {
            var response = await HandleLineAsync(line, serverName, serverVersion, tools, toolIndex, ct)
                .ConfigureAwait(false);

            if (response is null)
            {
                continue; // notification — sem resposta
            }

            await output.WriteLineAsync(response.ToJsonString().AsMemory(), ct).ConfigureAwait(false);
        }
    }

    private static async Task<JsonObject?> HandleLineAsync(
        string line,
        string serverName,
        string serverVersion,
        IReadOnlyList<McpTool> tools,
        IReadOnlyDictionary<string, McpTool> toolIndex,
        CancellationToken ct)
    {
        JsonNode? envelope;
        try
        {
            envelope = JsonNode.Parse(line);
        }
        catch (Exception ex)
        {
            return BuildError(id: null, ErrorParseError, ex.Message);
        }

        if (envelope is not JsonObject req)
        {
            return BuildError(id: null, ErrorInvalidRequest, "JSON-RPC envelope must be an object.");
        }

        var idNode = req["id"];
        var method = req["method"]?.GetValue<string>();
        var @params = req["params"];

        if (string.IsNullOrEmpty(method))
        {
            return BuildError(idNode, ErrorInvalidRequest, "Missing 'method' field.");
        }

        // Notifications têm method mas não têm id — não respondem.
        var isNotification = idNode is null;

        try
        {
            switch (method)
            {
                case "initialize":
                    return isNotification ? null : BuildResult(idNode, BuildInitializeResult(serverName, serverVersion));

                case "notifications/initialized":
                    // Ack silencioso — cliente terminou o handshake.
                    return null;

                case "tools/list":
                    return isNotification ? null : BuildResult(idNode, BuildToolsListResult(tools));

                case "tools/call":
                    if (isNotification)
                    {
                        return null;
                    }

                    return await HandleToolCallAsync(idNode, @params, toolIndex, ct).ConfigureAwait(false);

                default:
                    return isNotification
                        ? null
                        : BuildError(idNode, ErrorMethodNotFound, $"Method '{method}' is not supported.");
            }
        }
        catch (Exception ex) when (!isNotification)
        {
            return BuildError(idNode, ErrorInternal, ex.Message);
        }
    }

    private static async Task<JsonObject> HandleToolCallAsync(
        JsonNode? idNode,
        JsonNode? @params,
        IReadOnlyDictionary<string, McpTool> toolIndex,
        CancellationToken ct)
    {
        if (@params is not JsonObject callParams)
        {
            return BuildError(idNode, ErrorInvalidParams, "tools/call requires an object 'params'.");
        }

        var name = callParams["name"]?.GetValue<string>();
        if (string.IsNullOrEmpty(name))
        {
            return BuildError(idNode, ErrorInvalidParams, "tools/call requires 'name'.");
        }

        if (!toolIndex.TryGetValue(name, out var tool))
        {
            return BuildError(idNode, ErrorMethodNotFound, $"Tool '{name}' is not registered.");
        }

        var arguments = callParams["arguments"];

        try
        {
            var text = await tool.InvokeAsync(arguments, ct).ConfigureAwait(false);
            return BuildResult(idNode, BuildToolCallResult(text, isError: false));
        }
        catch (Exception ex)
        {
            // Convenção MCP: falha da tool NÃO vira JSON-RPC error, vira result com isError=true
            // pra que o LLM possa ler a mensagem e decidir o que fazer.
            return BuildResult(idNode, BuildToolCallResult($"{ex.GetType().Name}: {ex.Message}", isError: true));
        }
    }

    private static JsonObject BuildInitializeResult(string serverName, string serverVersion) => new()
    {
        ["protocolVersion"] = ProtocolVersion,
        ["capabilities"] = new JsonObject
        {
            ["tools"] = new JsonObject(),
        },
        ["serverInfo"] = new JsonObject
        {
            ["name"] = serverName,
            ["version"] = serverVersion,
        },
    };

    private static JsonObject BuildToolsListResult(IReadOnlyList<McpTool> tools)
    {
        var arr = new JsonArray();
        foreach (var t in tools)
        {
            arr.Add(new JsonObject
            {
                ["name"] = t.Name,
                ["description"] = t.Description,
                ["inputSchema"] = t.InputSchema.DeepClone(),
            });
        }

        return new JsonObject { ["tools"] = arr };
    }

    private static JsonObject BuildToolCallResult(string text, bool isError) => new()
    {
        ["content"] = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = text,
            },
        },
        ["isError"] = isError,
    };

    private static JsonObject BuildResult(JsonNode? id, JsonNode result) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["result"] = result,
    };

    private static JsonObject BuildError(JsonNode? id, int code, string message) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["error"] = new JsonObject
        {
            ["code"] = code,
            ["message"] = message,
        },
    };
}
