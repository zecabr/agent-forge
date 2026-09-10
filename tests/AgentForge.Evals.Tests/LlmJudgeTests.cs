using AgentForge.Core.Chat;
using AgentForge.Evals;
using AgentForge.Evals.Tests.Fakes;
using Xunit;

namespace AgentForge.Evals.Tests;

public class LlmJudgeTests
{
    [Fact]
    public void ParseJudgment_Extracts_Passed_And_Reason_From_Clean_Json()
    {
        var text = """{"passed": true, "reason": "menciona Brasília corretamente"}""";

        var judgment = LlmJudge.ParseJudgment(text);

        Assert.True(judgment.Passed);
        Assert.Contains("Brasília", judgment.Reason);
    }

    [Fact]
    public void ParseJudgment_Strips_Markdown_Code_Fence()
    {
        var text = """
            ```json
            {"passed": false, "reason": "não responde a pergunta"}
            ```
            """;

        var judgment = LlmJudge.ParseJudgment(text);

        Assert.False(judgment.Passed);
        Assert.Equal("não responde a pergunta", judgment.Reason);
    }

    [Fact]
    public void ParseJudgment_Returns_Fail_On_Invalid_Json()
    {
        var text = "the answer is yes";

        var judgment = LlmJudge.ParseJudgment(text);

        Assert.False(judgment.Passed);
        Assert.Contains("invalid JSON", judgment.Reason);
    }

    [Fact]
    public async Task JudgeAsync_Sends_System_And_User_Messages_To_Provider()
    {
        var provider = new FakeChatProvider(
            new ChatResponse(
                Content: [new TextBlock("""{"passed": true, "reason": "ok"}""")],
                StopReason: StopReason.EndTurn,
                Usage: new UsageStats(100, 30, 0.01m)));

        var judge = new LlmJudge(provider, "test-model");

        var result = await judge.JudgeAsync(
            userPrompt: "qual a capital do Brasil?",
            agentResponse: "Brasília",
            criterion: "menciona Brasília");

        Assert.True(result.Judgment.Passed);
        Assert.Equal(0.01m, result.Usage.CostUsd);
        Assert.Single(provider.CallLog);
        var req = provider.CallLog[0];
        Assert.Equal(2, req.Messages.Count);
        Assert.Equal(ChatRole.System, req.Messages[0].Role);
        Assert.Equal(ChatRole.User, req.Messages[1].Role);
    }

    [Fact]
    public void Constructor_Rejects_Empty_Model()
    {
        var provider = new FakeChatProvider();

        Assert.Throws<ArgumentException>(() => new LlmJudge(provider, ""));
        Assert.Throws<ArgumentException>(() => new LlmJudge(provider, "   "));
    }
}
