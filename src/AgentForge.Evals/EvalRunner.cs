using AgentForge.Core;
using AgentForge.Core.Chat;

namespace AgentForge.Evals;

/// <summary>
/// Orquestra a execução de <see cref="EvalCase"/>s: roda o agente, passa a resposta
/// pro judge, agrega custo, respeita <see cref="EvalBudget"/>.
/// Uso típico é dentro de testes xUnit — cada caso é 1 <c>[Fact]</c> ou linha de <c>[Theory]</c>.
/// </summary>
public sealed class EvalRunner
{
    private readonly Agent _agent;
    private readonly IJudge _judge;
    private readonly EvalBudget _budget;
    private UsageStats _totalUsage = UsageStats.Empty;

    public EvalRunner(Agent agent, IJudge judge, EvalBudget budget)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _judge = judge ?? throw new ArgumentNullException(nameof(judge));
        _budget = budget ?? throw new ArgumentNullException(nameof(budget));
    }

    /// <summary>Consumo acumulado (agente + judge) ao longo dos <c>RunAsync</c> desta instância.</summary>
    public UsageStats TotalUsage => _totalUsage;

    public async Task<EvalResult> RunAsync(EvalCase evalCase, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evalCase);

        if (_totalUsage.CostUsd > _budget.MaxTotalCostUsd)
        {
            return new EvalResult(
                CaseName: evalCase.Name,
                Passed: false,
                Reason: $"budget exceeded before this case (${_totalUsage.CostUsd:F4} > ${_budget.MaxTotalCostUsd:F4})",
                AgentResponse: null,
                AgentUsage: UsageStats.Empty,
                JudgeUsage: UsageStats.Empty,
                AgentSteps: 0);
        }

        // 1. roda o agente
        var session = new AgentSession(costCapUsd: _budget.MaxTotalCostUsd);
        var agentResult = await _agent.RunAsync(session, evalCase.UserPrompt, ct).ConfigureAwait(false);
        _totalUsage = _totalUsage.Add(agentResult.Usage);

        if (agentResult.Outcome != AgentOutcome.Success || agentResult.FinalText is null)
        {
            return new EvalResult(
                CaseName: evalCase.Name,
                Passed: false,
                Reason: $"agent outcome was {agentResult.Outcome}" +
                        (agentResult.Reason is null ? "" : $" — {agentResult.Reason}"),
                AgentResponse: agentResult.FinalText,
                AgentUsage: agentResult.Usage,
                JudgeUsage: UsageStats.Empty,
                AgentSteps: agentResult.Steps);
        }

        // 2. roda o judge
        var judgeResponse = await _judge.JudgeAsync(
            evalCase.UserPrompt, agentResult.FinalText, evalCase.Criterion, ct).ConfigureAwait(false);
        _totalUsage = _totalUsage.Add(judgeResponse.Usage);

        return new EvalResult(
            CaseName: evalCase.Name,
            Passed: judgeResponse.Judgment.Passed,
            Reason: judgeResponse.Judgment.Reason,
            AgentResponse: agentResult.FinalText,
            AgentUsage: agentResult.Usage,
            JudgeUsage: judgeResponse.Usage,
            AgentSteps: agentResult.Steps);
    }

    /// <summary>Roda todos os cases em ordem. Não interrompe entre eles — deixa quem chama decidir.</summary>
    public async Task<IReadOnlyList<EvalResult>> RunAllAsync(
        IEnumerable<EvalCase> cases,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cases);

        var results = new List<EvalResult>();
        foreach (var c in cases)
        {
            var r = await RunAsync(c, ct).ConfigureAwait(false);
            results.Add(r);
        }

        return results;
    }
}
