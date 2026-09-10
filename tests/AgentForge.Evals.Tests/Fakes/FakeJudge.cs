using AgentForge.Core.Chat;
using AgentForge.Evals;

namespace AgentForge.Evals.Tests.Fakes;

/// <summary>Judge fake com veredito fixo e usage configurável.</summary>
public sealed class FakeJudge : IJudge
{
    private readonly EvalJudgment _judgment;
    private readonly UsageStats _usage;
    private readonly List<(string Prompt, string Response, string Criterion)> _calls = [];

    public FakeJudge(bool passed, string reason, UsageStats? usage = null)
    {
        _judgment = new EvalJudgment(passed, reason);
        _usage = usage ?? new UsageStats(50, 25, 0.005m);
    }

    public IReadOnlyList<(string Prompt, string Response, string Criterion)> Calls => _calls;

    public Task<JudgeResponse> JudgeAsync(
        string userPrompt,
        string agentResponse,
        string criterion,
        CancellationToken ct = default)
    {
        _calls.Add((userPrompt, agentResponse, criterion));
        return Task.FromResult(new JudgeResponse(_judgment, _usage));
    }
}
