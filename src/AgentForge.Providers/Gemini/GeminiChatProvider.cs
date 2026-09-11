using System.Text;
using System.Text.Json.Nodes;
using AgentForge.Core.Abstractions;
using AgentForge.Core.Chat;

namespace AgentForge.Providers.Gemini;

/// <summary>
/// Adapter <see cref="IChatProvider"/> para a Gemini API (Google AI Studio).
/// Usa API key via header <c>x-goog-api-key</c>. Endpoint por modelo:
/// <c>POST /v1beta/models/{model}:generateContent</c>.
/// </summary>
public sealed class GeminiChatProvider : IChatProvider, IDisposable
{
    private const string DefaultBaseUri = "https://generativelanguage.googleapis.com";
    private const string ApiVersionPath = "/v1beta";

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly string _apiKey;
    private readonly string _baseUri;

    public GeminiChatProvider(
        string apiKey,
        HttpClient? httpClient = null,
        string? baseUri = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("API key must not be empty.", nameof(apiKey));
        }

        _apiKey = apiKey;
        _http = httpClient ?? new HttpClient();
        _ownsHttp = httpClient is null;
        _baseUri = (baseUri ?? DefaultBaseUri).TrimEnd('/');
    }

    public async Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var body = GeminiMapper.MapRequestToJson(request);
        var endpoint = new Uri($"{_baseUri}{ApiVersionPath}/models/{Uri.EscapeDataString(request.Model)}:generateContent");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        httpRequest.Headers.Add("x-goog-api-key", _apiKey);

        using var response = await _http.SendAsync(httpRequest, ct).ConfigureAwait(false);
        var responseText = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new GeminiApiException(
                $"Gemini API returned {(int)response.StatusCode} {response.StatusCode}: {responseText}",
                (int)response.StatusCode);
        }

        var json = JsonNode.Parse(responseText)
            ?? throw new GeminiApiException("Empty response body from Gemini API.", (int)response.StatusCode);

        return GeminiMapper.MapResponseFromJson(json, request.Model);
    }

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }
}
