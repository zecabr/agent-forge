using System.Text.Json.Nodes;

namespace AgentForge.Mcp.Server;

/// <summary>
/// Uma tool exposta por um servidor MCP. Cada capability implementa uma ou
/// mais dessas e registra num <see cref="McpStdioServer"/>.
/// </summary>
public abstract class McpTool
{
    /// <summary>
    /// Nome que o client vê e usa em <c>tools/call</c>.
    /// Convenção: snake_case (<c>search_docs</c>, <c>read_file</c>).
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// Descrição que o LLM lê pra decidir se chama a tool. Trate como prompt:
    /// diga o que a tool faz, quando usar, o que espera receber, o que devolve.
    /// </summary>
    public abstract string Description { get; }

    /// <summary>
    /// JSON Schema dos argumentos aceitos por <see cref="InvokeAsync"/>.
    /// Formato padrão do <c>inputSchema</c> do MCP: um objeto JSON Schema Draft-07.
    /// </summary>
    public abstract JsonObject InputSchema { get; }

    /// <summary>
    /// Executa a tool. Recebe os argumentos parseados como <see cref="JsonNode"/>
    /// (nulo se a chamada não enviou argumentos), devolve o texto de resultado.
    /// <para>
    /// Jogar exception aqui vira <c>isError: true</c> na resposta MCP com a mensagem
    /// da exception — o LLM lê e decide o que fazer. Prefira mensagens actionáveis
    /// ("arquivo X não encontrado, tente Y") a stack traces.
    /// </para>
    /// </summary>
    public abstract Task<string> InvokeAsync(JsonNode? arguments, CancellationToken ct);
}
