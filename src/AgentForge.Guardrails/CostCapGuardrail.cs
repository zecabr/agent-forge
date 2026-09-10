using System.Globalization;
using AgentForge.Core.Abstractions;
using AgentForge.Core.Guardrails;

namespace AgentForge.Guardrails;

/// <summary>
/// Guardrail de custo — bloqueia quando o custo acumulado da sessão ultrapassa
/// um limite explícito. É <em>complementar</em> ao check embutido no Agent.RunAsync,
/// que só age quando estoura o <see cref="Core.AgentSession.CostCapUsd"/>. Este
/// permite defesa em profundidade (ex.: cortar em 80% do teto pra evitar 1 turno
/// grande estourar de vez).
/// </summary>
public sealed class CostCapGuardrail : IGuardrail
{
    private readonly decimal _thresholdUsd;

    public CostCapGuardrail(decimal thresholdUsd)
    {
        if (thresholdUsd < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(thresholdUsd), thresholdUsd,
                "Threshold must be zero or positive.");
        }

        _thresholdUsd = thresholdUsd;
    }

    public string Name => "cost-cap";

    public Task<GuardrailResult> CheckAsync(GuardrailContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.CumulativeCostUsd > _thresholdUsd)
        {
            return Task.FromResult(GuardrailResult.Fail(
                string.Create(CultureInfo.InvariantCulture,
                    $"cumulative cost ${context.CumulativeCostUsd:F4} exceeds threshold ${_thresholdUsd:F4}")));
        }

        return Task.FromResult(GuardrailResult.Pass);
    }
}
