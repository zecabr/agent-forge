using AgentForge.Core.Chat;
using Xunit;

namespace AgentForge.Core.Tests.Chat;

public class UsageStatsTests
{
    [Fact]
    public void Empty_Has_Zero_Values()
    {
        var stats = UsageStats.Empty;

        Assert.Equal(0, stats.InputTokens);
        Assert.Equal(0, stats.OutputTokens);
        Assert.Equal(0m, stats.CostUsd);
    }

    [Fact]
    public void Add_Sums_All_Fields()
    {
        var a = new UsageStats(100, 50, 0.02m);
        var b = new UsageStats(200, 150, 0.05m);

        var sum = a.Add(b);

        Assert.Equal(300, sum.InputTokens);
        Assert.Equal(200, sum.OutputTokens);
        Assert.Equal(0.07m, sum.CostUsd);
    }

    [Fact]
    public void Add_Is_Immutable()
    {
        var a = new UsageStats(100, 50, 0.02m);
        var b = new UsageStats(1, 1, 0.01m);

        _ = a.Add(b);

        Assert.Equal(100, a.InputTokens);
        Assert.Equal(50, a.OutputTokens);
        Assert.Equal(0.02m, a.CostUsd);
    }
}
