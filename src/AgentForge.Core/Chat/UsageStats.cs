namespace AgentForge.Core.Chat;

/// <summary>
/// Consumo agregado de tokens e custo. Somável ao longo de uma sessão via <see cref="Add"/>.
/// </summary>
public sealed record UsageStats(int InputTokens, int OutputTokens, decimal CostUsd = 0m)
{
    public static UsageStats Empty { get; } = new(0, 0, 0m);

    public UsageStats Add(UsageStats other) => new(
        InputTokens + other.InputTokens,
        OutputTokens + other.OutputTokens,
        CostUsd + other.CostUsd);
}
