using AgentForge.Core;
using AgentForge.Core.Chat;
using AgentForge.Evals;
using AgentForge.Evals.Tests.Fakes;
using Xunit;

namespace AgentForge.Evals.Tests;

public class EvalRunnerTests
{
    private static Agent BuildAgent(string finalText, decimal costUsd = 0.01m)
    {
        var provider = new FakeChatProvider(
            new ChatResponse(
                Content: [new TextBlock(finalText)],
                StopReason: StopReason.EndTurn,
                Usage: new UsageStats(100, 50, costUsd)));

        return new Agent(provider, options: new AgentOptions("test-model"));
    }

    [Fact]
    public async Task Passes_When_Agent_Succeeds_And_Judge_Approves()
    {
        var agent = BuildAgent("Brasília");
        var judge = new FakeJudge(passed: true, reason: "correto");
        var runner = new EvalRunner(agent, judge, EvalBudget.FromUsd(1.00m));

        var result = await runner.RunAsync(new EvalCase(
            Name: "capital-brasil",
            UserPrompt: "qual a capital do Brasil?",
            Criterion: "menciona Brasília"));

        Assert.True(result.Passed);
        Assert.Equal("correto", result.Reason);
        Assert.Equal("Brasília", result.AgentResponse);
        Assert.Equal(0.01m, result.AgentUsage.CostUsd);
        Assert.Equal(0.005m, result.JudgeUsage.CostUsd);
        Assert.Equal(0.015m, result.TotalUsage.CostUsd);
    }

    [Fact]
    public async Task Fails_When_Judge_Rejects_Even_If_Agent_Succeeded()
    {
        var agent = BuildAgent("Rio de Janeiro");
        var judge = new FakeJudge(passed: false, reason: "menciona cidade errada");
        var runner = new EvalRunner(agent, judge, EvalBudget.FromUsd(1.00m));

        var result = await runner.RunAsync(new EvalCase(
            "capital-brasil", "qual a capital?", "menciona Brasília"));

        Assert.False(result.Passed);
        Assert.Contains("cidade errada", result.Reason);
    }

    [Fact]
    public async Task Fails_When_Agent_Blocked_By_Guardrail()
    {
        // Provider ok, mas o agente tem guardrail que sempre falha
        var provider = new FakeChatProvider();  // sem respostas — se chamado, throws
        var guardrail = new FailingGuardrail();
        var agent = new Agent(provider, guardrails: [guardrail], options: new AgentOptions("test-model"));
        var judge = new FakeJudge(passed: true, reason: "irrelevante");
        var runner = new EvalRunner(agent, judge, EvalBudget.FromUsd(1.00m));

        var result = await runner.RunAsync(new EvalCase(
            "test", "any prompt", "any criterion"));

        Assert.False(result.Passed);
        Assert.Contains("BlockedByGuardrail", result.Reason);
        Assert.Empty(judge.Calls);
    }

    [Fact]
    public async Task Fails_When_Budget_Exceeded_Before_Case()
    {
        // Provider caro que estora o budget num único case
        var agent = BuildAgent("resposta", costUsd: 0.60m);
        var judge = new FakeJudge(passed: true, reason: "ok", usage: new UsageStats(10, 5, 0.60m));
        var runner = new EvalRunner(agent, judge, EvalBudget.FromUsd(1.00m));

        // 1º case: consome 0.60 + 0.60 = 1.20 (acima do budget). Passa porque estourou DURANTE o case.
        var first = await runner.RunAsync(new EvalCase("first", "q1", "c1"));
        Assert.True(first.Passed);

        // 2º case: já entra com _totalUsage acima do budget, então nem roda
        var second = await runner.RunAsync(new EvalCase("second", "q2", "c2"));
        Assert.False(second.Passed);
        Assert.Contains("budget exceeded", second.Reason);
        Assert.Null(second.AgentResponse);
    }

    [Fact]
    public async Task RunAll_Executes_All_Cases_In_Order()
    {
        var provider = new FakeChatProvider(
            new ChatResponse([new TextBlock("resp1")], StopReason.EndTurn, new UsageStats(10, 5, 0.001m)),
            new ChatResponse([new TextBlock("resp2")], StopReason.EndTurn, new UsageStats(10, 5, 0.001m)),
            new ChatResponse([new TextBlock("resp3")], StopReason.EndTurn, new UsageStats(10, 5, 0.001m)));
        var agent = new Agent(provider, options: new AgentOptions("test-model"));
        var judge = new FakeJudge(passed: true, reason: "ok", usage: new UsageStats(5, 2, 0.001m));
        var runner = new EvalRunner(agent, judge, EvalBudget.FromUsd(1.00m));

        var results = await runner.RunAllAsync([
            new EvalCase("c1", "q1", "crit"),
            new EvalCase("c2", "q2", "crit"),
            new EvalCase("c3", "q3", "crit"),
        ]);

        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.True(r.Passed));
        Assert.Equal(new[] { "c1", "c2", "c3" }, results.Select(r => r.CaseName));
    }

    [Fact]
    public async Task TotalUsage_Accumulates_Across_Cases()
    {
        var provider = new FakeChatProvider(
            new ChatResponse([new TextBlock("a")], StopReason.EndTurn, new UsageStats(10, 5, 0.01m)),
            new ChatResponse([new TextBlock("b")], StopReason.EndTurn, new UsageStats(10, 5, 0.01m)));
        var agent = new Agent(provider, options: new AgentOptions("test-model"));
        var judge = new FakeJudge(passed: true, reason: "ok", usage: new UsageStats(5, 2, 0.005m));
        var runner = new EvalRunner(agent, judge, EvalBudget.FromUsd(1.00m));

        await runner.RunAsync(new EvalCase("c1", "q1", "crit"));
        await runner.RunAsync(new EvalCase("c2", "q2", "crit"));

        Assert.Equal(0.03m, runner.TotalUsage.CostUsd); // 0.01+0.005+0.01+0.005
    }

    /// <summary>Guardrail que sempre bloqueia — só pra teste.</summary>
    private sealed class FailingGuardrail : Core.Abstractions.IGuardrail
    {
        public string Name => "always-fails";

        public Task<Core.Guardrails.GuardrailResult> CheckAsync(
            Core.Abstractions.GuardrailContext context,
            CancellationToken ct = default) =>
            Task.FromResult(Core.Guardrails.GuardrailResult.Fail("blocked by test"));
    }
}
