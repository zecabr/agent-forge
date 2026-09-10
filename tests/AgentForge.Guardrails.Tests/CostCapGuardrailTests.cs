using AgentForge.Core.Abstractions;
using AgentForge.Guardrails;
using Xunit;

namespace AgentForge.Guardrails.Tests;

public class CostCapGuardrailTests
{
    private static GuardrailContext Ctx(decimal cumulativeCost) =>
        new(GuardrailStage.PreTurn, [], cumulativeCost);

    [Fact]
    public async Task Passes_When_Cost_Under_Threshold()
    {
        var guard = new CostCapGuardrail(thresholdUsd: 1.00m);

        var result = await guard.CheckAsync(Ctx(0.50m));

        Assert.True(result.Passed);
    }

    [Fact]
    public async Task Passes_When_Cost_Equals_Threshold()
    {
        var guard = new CostCapGuardrail(thresholdUsd: 1.00m);

        var result = await guard.CheckAsync(Ctx(1.00m));

        // usa > (não >=), então igual passa
        Assert.True(result.Passed);
    }

    [Fact]
    public async Task Fails_When_Cost_Above_Threshold()
    {
        var guard = new CostCapGuardrail(thresholdUsd: 1.00m);

        var result = await guard.CheckAsync(Ctx(1.50m));

        Assert.False(result.Passed);
        Assert.Contains("1.5000", result.Reason!);
        Assert.Contains("1.0000", result.Reason!);
    }

    [Fact]
    public void Constructor_Rejects_Negative_Threshold()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CostCapGuardrail(-0.01m));
    }
}
