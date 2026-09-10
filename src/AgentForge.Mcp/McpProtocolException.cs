namespace AgentForge.Mcp;

/// <summary>
/// Erro devolvido pelo servidor MCP no envelope JSON-RPC (campo "error").
/// </summary>
public sealed class McpProtocolException : Exception
{
    public McpProtocolException()
    {
    }

    public McpProtocolException(string message)
        : base(message)
    {
    }

    public McpProtocolException(string message, Exception inner)
        : base(message, inner)
    {
    }

    public McpProtocolException(string message, int code)
        : base(message)
    {
        Code = code;
    }

    public int Code { get; }
}
