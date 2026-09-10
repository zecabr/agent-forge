using AgentForge.Core.Abstractions;
using AgentForge.Core.Guardrails;

namespace AgentForge.Core.Tests.Fakes;

/// <summary>
/// Guardrail que devolve sempre o mesmo resultado. Registra todos os contextos vistos.
/// </summary>
public sealed class FakeGuardrail : IGuardrail
{
    private readonly GuardrailResult _fixedResult;
    private readonly List<GuardrailContext> _contextsSeen = [];

    public FakeGuardrail(string name, GuardrailResult fixedResult)
    {
        Name = name;
        _fixedResult = fixedResult;
    }

    public string Name { get; }

    public IReadOnlyList<GuardrailContext> ContextsSeen => _contextsSeen;

    public Task<GuardrailResult> CheckAsync(GuardrailContext context, CancellationToken ct = default)
    {
        _contextsSeen.Add(context);
        return Task.FromResult(_fixedResult);
    }
}
