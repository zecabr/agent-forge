using AgentForge.Core.Abstractions;
using AgentForge.Core.Chat;

namespace AgentForge.Core.Tests.Fakes;

/// <summary>
/// Devolve respostas pré-programadas em ordem FIFO.
/// Se acabar antes do agent parar, joga InvalidOperationException — sinal de bug no teste.
/// </summary>
public sealed class FakeChatProvider : IChatProvider
{
    private readonly Queue<ChatResponse> _responses;
    private readonly List<ChatRequest> _callLog = [];

    public FakeChatProvider(params ChatResponse[] responses)
    {
        _responses = new Queue<ChatResponse>(responses);
    }

    public IReadOnlyList<ChatRequest> CallLog => _callLog;

    public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken ct = default)
    {
        _callLog.Add(request);
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException(
                "FakeChatProvider ran out of pre-programmed responses.");
        }

        return Task.FromResult(_responses.Dequeue());
    }
}
