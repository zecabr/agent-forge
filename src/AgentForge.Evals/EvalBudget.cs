namespace AgentForge.Evals;

/// <summary>
/// Orçamento de custo pra uma suite de evals. Runner interrompe quando
/// estourar — protege CI que sangra dinheiro por bug (ADR-002).
/// </summary>
public sealed record EvalBudget(decimal MaxTotalCostUsd)
{
    public static EvalBudget FromUsd(decimal amount) => new(amount);
}
