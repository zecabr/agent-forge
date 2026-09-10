using System.Text.Json.Nodes;
using AgentForge.Mcp.Transport;

namespace AgentForge.Mcp.Tests.Fakes;

/// <summary>
/// Transport MCP fake: registra chamadas e devolve respostas pré-programadas
/// por método. Métodos sem resposta registrada devolvem null.
/// </summary>
public sealed class FakeMcpTransport : IMcpTransport
{
    private readonly List<(string Method, JsonNode? Params)> _requestLog = [];
    private readonly List<(string Method, JsonNode? Params)> _notificationLog = [];

    public FakeMcpTransport(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public bool Started { get; private set; }

    public bool DisposeCalled { get; private set; }

    public Dictionary<string, JsonNode?> ResponseByMethod { get; } = [];

    public IReadOnlyList<(string Method, JsonNode? Params)> RequestLog => _requestLog;

    public IReadOnlyList<(string Method, JsonNode? Params)> NotificationLog => _notificationLog;

    public Task StartAsync(CancellationToken ct = default)
    {
        Started = true;
        return Task.CompletedTask;
    }

    public Task<JsonNode?> SendRequestAsync(string method, JsonNode? @params, CancellationToken ct = default)
    {
        _requestLog.Add((method, @params));
        ResponseByMethod.TryGetValue(method, out var response);
        return Task.FromResult(response);
    }

    public Task SendNotificationAsync(string method, JsonNode? @params, CancellationToken ct = default)
    {
        _notificationLog.Add((method, @params));
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        DisposeCalled = true;
        return ValueTask.CompletedTask;
    }
}
