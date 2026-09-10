using System.Text;
using System.Text.Json.Nodes;
using AgentForge.Core.Abstractions;
using AgentForge.Core.Chat;

namespace AgentForge.Providers.Anthropic;

/// <summary>
/// Adapter <see cref="IChatProvider"/> para a API Anthropic Messages.
/// Recebe HttpClient opcional — passe um configurado externamente para
/// controlar retry, timeout, DI, ou para uso em testes com handler custom.
/// </summary>
public sealed class AnthropicChatProvider : IChatProvider, IDisposable
{
    private const string DefaultBaseUri = "https://api.anthropic.com";
    private const string ApiVersion = "2023-06-01";

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly string _apiKey;
    private readonly Uri _endpoint;

    public AnthropicChatProvider(
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
        _endpoint = new Uri(new Uri(baseUri ?? DefaultBaseUri), "/v1/messages");
    }

    public async Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var body = AnthropicMapper.MapRequestToJson(request);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        httpRequest.Headers.Add("x-api-key", _apiKey);
        httpRequest.Headers.Add("anthropic-version", ApiVersion);

        using var response = await _http.SendAsync(httpRequest, ct).ConfigureAwait(false);
        var responseText = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new AnthropicApiException(
                $"Anthropic API returned {(int)response.StatusCode} {response.StatusCode}: {responseText}",
                (int)response.StatusCode);
        }

        var json = JsonNode.Parse(responseText)
            ?? throw new AnthropicApiException("Empty response body from Anthropic API.", (int)response.StatusCode);

        return AnthropicMapper.MapResponseFromJson(json);
    }

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }
}
