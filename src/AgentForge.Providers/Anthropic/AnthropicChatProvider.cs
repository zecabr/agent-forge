using System.Text;
using System.Text.Json.Nodes;
using AgentForge.Core.Abstractions;
using AgentForge.Core.Chat;

namespace AgentForge.Providers.Anthropic;

/// <summary>
/// Adapter <see cref="IChatProvider"/> para a API Anthropic Messages.
/// Recebe HttpClient opcional — passe um configurado externamente para
/// controlar retry, timeout, DI, ou para uso em testes com handler custom.
/// <para>
/// Se sua API key for org-level (não scoped a um workspace específico), a API
/// exige o header <c>anthropic-workspace-id</c>. Passe o ID via <paramref name="workspaceId"/>
/// no construtor ou use uma key scoped a workspace.
/// </para>
/// </summary>
public sealed class AnthropicChatProvider : IChatProvider, IDisposable
{
    private const string DefaultBaseUri = "https://api.anthropic.com";
    private const string ApiVersion = "2023-06-01";

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly string _apiKey;
    private readonly Uri _endpoint;
    private readonly string? _workspaceId;

    public AnthropicChatProvider(
        string apiKey,
        HttpClient? httpClient = null,
        string? baseUri = null,
        string? workspaceId = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("API key must not be empty.", nameof(apiKey));
        }

        _apiKey = apiKey;
        _http = httpClient ?? new HttpClient();
        _ownsHttp = httpClient is null;
        _endpoint = new Uri(new Uri(baseUri ?? DefaultBaseUri), "/v1/messages");
        _workspaceId = string.IsNullOrWhiteSpace(workspaceId) ? null : workspaceId;
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

        if (_workspaceId is not null)
        {
            httpRequest.Headers.Add("anthropic-workspace-id", _workspaceId);
        }

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
