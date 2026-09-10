using System.Net;
using System.Text;

namespace AgentForge.Providers.Tests.Fakes;

/// <summary>
/// HttpMessageHandler que grava todas as requisições feitas e devolve
/// uma resposta pré-programada. Suporta múltiplas respostas em ordem FIFO.
/// </summary>
public sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> _responses;
    private readonly List<(HttpRequestMessage Request, string RequestBody)> _calls = [];

    public RecordingHandler(HttpStatusCode status, string body)
    {
        _responses = new Queue<(HttpStatusCode, string)>([(status, body)]);
    }

    public RecordingHandler(params (HttpStatusCode Status, string Body)[] responses)
    {
        _responses = new Queue<(HttpStatusCode, string)>(responses);
    }

    public IReadOnlyList<(HttpRequestMessage Request, string RequestBody)> Calls => _calls;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken ct)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        _calls.Add((request, body));

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("RecordingHandler ran out of pre-programmed responses.");
        }

        var (status, responseBody) = _responses.Dequeue();
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
        };
    }
}
