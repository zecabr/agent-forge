namespace AgentForge.Mcp;

/// <summary>
/// Configuração de um servidor MCP a ser iniciado pelo cliente.
/// Formato compatível com o "mcpServers" config do Claude Desktop e do Continue.
/// </summary>
public sealed record McpServerConfig(
    string Name,
    string Command,
    IReadOnlyList<string>? Args = null,
    IReadOnlyDictionary<string, string>? EnvironmentVariables = null);
