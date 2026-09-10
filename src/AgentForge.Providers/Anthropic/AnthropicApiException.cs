namespace AgentForge.Providers.Anthropic;

/// <summary>
/// Erro devolvido pela API Anthropic (status != 2xx). Carrega o código HTTP.
/// </summary>
public sealed class AnthropicApiException : Exception
{
    public AnthropicApiException()
    {
    }

    public AnthropicApiException(string message)
        : base(message)
    {
    }

    public AnthropicApiException(string message, Exception inner)
        : base(message, inner)
    {
    }

    public AnthropicApiException(string message, int statusCode)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
