namespace AgentForge.Providers.Gemini;

/// <summary>
/// Erro devolvido pela Gemini API (status != 2xx). Carrega o código HTTP.
/// </summary>
public sealed class GeminiApiException : Exception
{
    public GeminiApiException()
    {
    }

    public GeminiApiException(string message)
        : base(message)
    {
    }

    public GeminiApiException(string message, Exception inner)
        : base(message, inner)
    {
    }

    public GeminiApiException(string message, int statusCode)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
