using AgentForge.Core.Chat;
using Xunit;

namespace AgentForge.Core.Tests;

public class AgentSessionTests
{
    [Fact]
    public void New_Session_Has_Empty_Messages_And_Zero_Usage()
    {
        var s = new AgentSession();

        Assert.Empty(s.Messages);
        Assert.Equal(UsageStats.Empty, s.CumulativeUsage);
        Assert.NotEmpty(s.Id);
    }

    [Fact]
    public void AppendMessage_Adds_To_History_In_Order()
    {
        var s = new AgentSession();

        s.AppendMessage(ChatMessage.User("oi"));
        s.AppendMessage(ChatMessage.Assistant("olá"));

        Assert.Equal(2, s.Messages.Count);
        Assert.Equal(ChatRole.User, s.Messages[0].Role);
        Assert.Equal(ChatRole.Assistant, s.Messages[1].Role);
    }

    [Fact]
    public void RecordUsage_Accumulates_Across_Calls()
    {
        var s = new AgentSession();

        s.RecordUsage(new UsageStats(10, 5, 0.01m));
        s.RecordUsage(new UsageStats(20, 5, 0.02m));

        Assert.Equal(30, s.CumulativeUsage.InputTokens);
        Assert.Equal(10, s.CumulativeUsage.OutputTokens);
        Assert.Equal(0.03m, s.CumulativeUsage.CostUsd);
    }

    [Fact]
    public void ExceededCostCap_Is_True_When_Usage_Passes_Threshold()
    {
        var s = new AgentSession(costCapUsd: 0.05m);

        s.RecordUsage(new UsageStats(10, 5, 0.06m));

        Assert.True(s.ExceededCostCap);
    }

    [Fact]
    public void ExceededCostCap_Is_False_When_Usage_Is_Below_Threshold()
    {
        var s = new AgentSession(costCapUsd: 0.10m);

        s.RecordUsage(new UsageStats(10, 5, 0.05m));

        Assert.False(s.ExceededCostCap);
    }
}
