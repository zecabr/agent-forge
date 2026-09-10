using AgentForge.Core.Abstractions;
using AgentForge.Core.Chat;

namespace AgentForge.Evals.Tests.Fakes;

/// <summary>FakeChatProvider local — duplica o do Core.Tests porque é internal lá.</summary>
public sealed class FakeChatProvider : IChatProvider
{
    private readonly Queue<ChatResponse> _responses;
    private readonly List<ChatRequest> _log = [];

    public FakeChatProvider(params ChatResponse[] responses)
    {
        _responses = new Queue<ChatResponse>(responses);
    }

    public IReadOnlyList<ChatRequest> CallLog => _log;

    public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken ct = default)
    {
        _log.Add(request);
        return _responses.Count == 0
            ? throw new InvalidOperationException("FakeChatProvider out of responses")
            : Task.FromResult(_responses.Dequeue());
    }
}
