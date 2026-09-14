using System.Text.Json.Nodes;
using AgentForge.Core.Abstractions;
using AgentForge.Core.Chat;
using AgentForge.Core.Tools;
using AgentForge.Mcp.Transport;

namespace AgentForge.Mcp;

/// <summary>
/// Cliente MCP que agrega múltiplos servidores via <see cref="IMcpTransport"/>.
/// Implementa <see cref="IMcpClient"/> do Core — sob o ponto de vista do agent loop,
/// vários servidores aparecem como um único catálogo de tools.
/// Lazy: só inicia processos e chama initialize na primeira <see cref="DiscoverToolsAsync"/> ou <see cref="InvokeAsync"/>.
/// </summary>
/// <remarks>
/// Robustez: se um server declarado no config falhar ao subir (StartAsync throw, initialize throw),
/// o McpHost <b>loga o erro no diagnosticLog e segue com os demais servers</b>. Um server quebrado
/// não derruba o host inteiro — a lista final de tools reflete o que efetivamente subiu.
/// O default do diagnosticLog é <see cref="Console.Error"/>; injete um logger custom nos testes.
/// </remarks>
public sealed class McpHost : IMcpClient, IAsyncDisposable
{
    private const string ProtocolVersion = "2024-11-05";

    private readonly IReadOnlyList<McpServerConfig> _configs;
    private readonly Func<McpServerConfig, IMcpTransport> _transportFactory;
    private readonly Action<string> _diagnosticLog;
    private readonly List<IMcpTransport> _transports = [];
    private readonly Dictionary<string, IMcpTransport> _toolToTransport = [];
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;
    private bool _disposed;

    public McpHost(
        IEnumerable<McpServerConfig> servers,
        Func<McpServerConfig, IMcpTransport>? transportFactory = null,
        Action<string>? diagnosticLog = null)
    {
        ArgumentNullException.ThrowIfNull(servers);
        _configs = [.. servers];
        _transportFactory = transportFactory ?? (cfg => new StdioTransport(cfg));
        _diagnosticLog = diagnosticLog ?? (msg => Console.Error.WriteLine(msg));
    }

    public async Task<IReadOnlyList<ToolDefinition>> DiscoverToolsAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);

        var tools = new List<ToolDefinition>();

        foreach (var transport in _transports)
        {
            var result = await transport.SendRequestAsync("tools/list", @params: null, ct).ConfigureAwait(false);
            var toolsArr = result?["tools"]?.AsArray();
            if (toolsArr is null)
            {
                continue;
            }

            foreach (var toolNode in toolsArr)
            {
                if (toolNode is null)
                {
                    continue;
                }

                var name = toolNode["name"]?.GetValue<string>();
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var description = toolNode["description"]?.GetValue<string>() ?? string.Empty;
                var schema = toolNode["inputSchema"]?.ToJsonString() ?? "{}";

                tools.Add(new ToolDefinition(name, description, schema));
                _toolToTransport[name] = transport;
            }
        }

        return tools;
    }

    public async Task<ToolResultBlock> InvokeAsync(
        string toolUseId,
        string toolName,
        string argumentsJson,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);

        if (_toolToTransport.Count == 0)
        {
            // discover ainda não rodou nesta sessão — força uma varredura
            _ = await DiscoverToolsAsync(ct).ConfigureAwait(false);
        }

        if (!_toolToTransport.TryGetValue(toolName, out var transport))
        {
            return new ToolResultBlock(
                toolUseId,
                $"tool '{toolName}' not found in connected MCP servers",
                IsError: true);
        }

        try
        {
            JsonNode? arguments = null;
            if (!string.IsNullOrWhiteSpace(argumentsJson))
            {
                arguments = JsonNode.Parse(argumentsJson);
            }

            var @params = new JsonObject
            {
                ["name"] = toolName,
                ["arguments"] = arguments ?? new JsonObject(),
            };

            var result = await transport.SendRequestAsync("tools/call", @params, ct).ConfigureAwait(false);

            var contentText = ExtractContentText(result?["content"]?.AsArray());
            var isError = result?["isError"]?.GetValue<bool>() ?? false;

            return new ToolResultBlock(toolUseId, contentText, isError);
        }
        catch (McpProtocolException ex)
        {
            return new ToolResultBlock(toolUseId, $"MCP error: {ex.Message}", IsError: true);
        }
    }

    private static string ExtractContentText(JsonArray? contentArr)
    {
        if (contentArr is null)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var block in contentArr)
        {
            var type = block?["type"]?.GetValue<string>();
            if (type == "text")
            {
                parts.Add(block!["text"]?.GetValue<string>() ?? string.Empty);
            }
        }

        return string.Join("\n", parts);
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized)
        {
            return;
        }

        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            foreach (var config in _configs)
            {
                IMcpTransport? transport = null;
                try
                {
                    transport = _transportFactory(config);
                    await transport.StartAsync(ct).ConfigureAwait(false);

                    var initParams = new JsonObject
                    {
                        ["protocolVersion"] = ProtocolVersion,
                        ["capabilities"] = new JsonObject(),
                        ["clientInfo"] = new JsonObject
                        {
                            ["name"] = "agent-forge",
                            ["version"] = "0.1.0",
                        },
                    };

                    _ = await transport.SendRequestAsync("initialize", initParams, ct).ConfigureAwait(false);
                    await transport.SendNotificationAsync("notifications/initialized", @params: null, ct).ConfigureAwait(false);

                    _transports.Add(transport);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // cancelamento cooperativo: dispose do transport parcial e propaga
                    await SafeDisposeAsync(transport).ConfigureAwait(false);
                    throw;
                }
                catch (Exception ex)
                {
                    // um server que não subiu não derruba os demais — loga e segue
                    _diagnosticLog(
                        $"[mcp-host] server '{config.Name}' failed to start ({ex.GetType().Name}): {ex.Message}");
                    await SafeDisposeAsync(transport).ConfigureAwait(false);
                }
            }

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private static async Task SafeDisposeAsync(IMcpTransport? transport)
    {
        if (transport is null)
        {
            return;
        }

        try
        {
            await transport.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // best-effort — nada útil a fazer se o dispose de um transport quebrado falhar
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var transport in _transports)
        {
            try
            {
                await transport.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // best-effort
            }
        }

        _initLock.Dispose();
    }
}
