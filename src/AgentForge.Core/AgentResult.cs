using AgentForge.Core.Chat;

namespace AgentForge.Core;

/// <summary>
/// Resultado de uma execução de <see cref="Agent.RunAsync"/>.
/// <see cref="Usage"/> é o total acumulado até o encerramento, mesmo em falhas.
/// </summary>
public sealed record AgentResult(
    AgentOutcome Outcome,
    string? FinalText,
    UsageStats Usage,
    int Steps,
    string? Reason = null)
{
    public static AgentResult Success(string text, UsageStats usage, int steps) =>
        new(AgentOutcome.Success, text, usage, steps);

    public static AgentResult Blocked(string reason, UsageStats usage, int steps) =>
        new(AgentOutcome.BlockedByGuardrail, null, usage, steps, reason);

    public static AgentResult CostCapExceeded(UsageStats usage, int steps) =>
        new(AgentOutcome.CostCapExceeded, null, usage, steps);

    public static AgentResult MaxStepsReached(UsageStats usage, int steps) =>
        new(AgentOutcome.MaxStepsReached, null, usage, steps);

    public static AgentResult Error(string reason, UsageStats usage, int steps) =>
        new(AgentOutcome.Error, null, usage, steps, reason);
}

public enum AgentOutcome
{
    /// <summary>Modelo terminou o turno naturalmente e devolveu resposta final.</summary>
    Success,

    /// <summary>Um guardrail interrompeu o loop.</summary>
    BlockedByGuardrail,

    /// <summary>Custo acumulado ultrapassou o cap da sessão.</summary>
    CostCapExceeded,

    /// <summary>Loop chegou ao <see cref="AgentOptions.MaxSteps"/> sem convergir.</summary>
    MaxStepsReached,

    /// <summary>Erro de contrato (ex.: modelo pediu tool sem IMcpClient configurado).</summary>
    Error,
}
