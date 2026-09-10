using AgentForge.Core.Chat;

namespace AgentForge.Evals;

/// <summary>
/// Resultado agregado de um caso: veredito, resposta do agente,
/// custos separados de agente e judge, e passos que o loop deu.
/// </summary>
public sealed record EvalResult(
    string CaseName,
    bool Passed,
    string Reason,
    string? AgentResponse,
    UsageStats AgentUsage,
    UsageStats JudgeUsage,
    int AgentSteps)
{
    public UsageStats TotalUsage => AgentUsage.Add(JudgeUsage);
}
