using AgentForge.Core.Chat;
using AgentForge.Core.Tools;

namespace AgentForge.Core.Abstractions;

/// <summary>
/// Cliente MCP mínimo consumido pelo agent loop. Wrapper interno sobre o
/// SDK oficial ModelContextProtocol (ver ADR-001) — a interface fica pequena
/// de propósito, pra proteger o Core de churn de API do SDK.
/// </summary>
public interface IMcpClient
{
    /// <summary>Descobre as tools expostas pelos servidores MCP conectados.</summary>
    Task<IReadOnlyList<ToolDefinition>> DiscoverToolsAsync(CancellationToken ct = default);

    /// <summary>Invoca uma tool e devolve o resultado como bloco de conteúdo pronto pra próxima mensagem.</summary>
    Task<ToolResultBlock> InvokeAsync(string toolUseId, string toolName, string argumentsJson, CancellationToken ct = default);
}
