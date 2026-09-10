using System.Text.Json.Nodes;

namespace AgentForge.Mcp.Transport;

/// <summary>
/// Transporte JSON-RPC 2.0 pra um servidor MCP. Implementações típicas:
/// <see cref="StdioTransport"/> (spawn + pipe) e (futuro) HttpTransport.
/// </summary>
public interface IMcpTransport : IAsyncDisposable
{
    /// <summary>Nome amigável do servidor (do <see cref="McpServerConfig.Name"/>).</summary>
    string Name { get; }

    /// <summary>Inicia o transporte. Depois disso o servidor está pronto pra receber requests.</summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>Envia request JSON-RPC e aguarda resposta correlacionada por id.</summary>
    Task<JsonNode?> SendRequestAsync(string method, JsonNode? @params, CancellationToken ct = default);

    /// <summary>Envia notificação JSON-RPC (fire-and-forget, sem resposta).</summary>
    Task SendNotificationAsync(string method, JsonNode? @params, CancellationToken ct = default);
}
