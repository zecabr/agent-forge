using AgentForge.Core.Abstractions;
using AgentForge.Core.Chat;
using AgentForge.Core.Guardrails;
using AgentForge.Core.Tests.Fakes;
using AgentForge.Core.Tools;
using Xunit;

namespace AgentForge.Core.Tests;

public class AgentTests
{
    private readonly AgentOptions _opts = new("test-model");

    [Fact]
    public async Task Simple_Turn_Without_Tools_Returns_Text()
    {
        var provider = new FakeChatProvider(
            new ChatResponse(
                Content: [new TextBlock("olá!")],
                StopReason: StopReason.EndTurn,
                Usage: new UsageStats(10, 5, 0.01m)));

        var agent = new Agent(provider, options: _opts);
        var session = new AgentSession();

        var result = await agent.RunAsync(session, "oi");

        Assert.Equal(AgentOutcome.Success, result.Outcome);
        Assert.Equal("olá!", result.FinalText);
        Assert.Equal(1, result.Steps);
        Assert.Equal(0.01m, result.Usage.CostUsd);
        Assert.Single(provider.CallLog);
    }

    [Fact]
    public async Task Tool_Use_Invokes_Mcp_And_Continues_Loop()
    {
        var provider = new FakeChatProvider(
            new ChatResponse(
                Content: [new ToolUseBlock("tu_1", "search", """{"q":"mcp"}""")],
                StopReason: StopReason.ToolUse,
                Usage: new UsageStats(10, 5, 0.01m)),
            new ChatResponse(
                Content: [new TextBlock("achei 3 resultados")],
                StopReason: StopReason.EndTurn,
                Usage: new UsageStats(20, 5, 0.02m)));

        var mcp = new FakeMcpClient
        {
            Tools = [new ToolDefinition("search", "web search", """{"type":"object"}""")],
        };
        mcp.Invocations["search"] = _ => """{"hits": 3}""";

        var agent = new Agent(provider, mcp, options: _opts);
        var session = new AgentSession();

        var result = await agent.RunAsync(session, "quantos resultados de mcp");

        Assert.Equal(AgentOutcome.Success, result.Outcome);
        Assert.Equal("achei 3 resultados", result.FinalText);
        Assert.Equal(2, result.Steps);
        Assert.Single(mcp.InvocationLog);
        Assert.Equal("search", mcp.InvocationLog[0].ToolName);
        Assert.Equal(0.03m, result.Usage.CostUsd);
    }

    [Fact]
    public async Task PreTurn_Guardrail_Blocks_Before_Provider_Is_Called()
    {
        var provider = new FakeChatProvider(); // sem respostas — se chamado, throws
        var guardrail = new FakeGuardrail("secret-detector", GuardrailResult.Fail("contém segredo"));

        var agent = new Agent(provider, guardrails: [guardrail], options: _opts);
        var session = new AgentSession();

        var result = await agent.RunAsync(session, "informação sensível");

        Assert.Equal(AgentOutcome.BlockedByGuardrail, result.Outcome);
        Assert.Equal("contém segredo", result.Reason);
        Assert.Empty(provider.CallLog);
        Assert.Single(guardrail.ContextsSeen);
        Assert.Equal(GuardrailStage.PreTurn, guardrail.ContextsSeen[0].Stage);
    }

    [Fact]
    public async Task PostTurn_Guardrail_Blocks_After_First_Response()
    {
        var provider = new FakeChatProvider(
            new ChatResponse(
                Content: [new TextBlock("resposta suspeita")],
                StopReason: StopReason.EndTurn,
                Usage: new UsageStats(10, 5, 0.01m)));

        var preGuard = new FakeGuardrail("pre", GuardrailResult.Pass);
        var postGuard = new PostOnlyGuardrail("post", GuardrailResult.Fail("output flagged"));

        var agent = new Agent(provider, guardrails: [preGuard, postGuard], options: _opts);
        var session = new AgentSession();

        var result = await agent.RunAsync(session, "pergunta");

        Assert.Equal(AgentOutcome.BlockedByGuardrail, result.Outcome);
        Assert.Equal("output flagged", result.Reason);
        Assert.Single(provider.CallLog);
    }

    [Fact]
    public async Task Cost_Cap_Exceeded_Stops_Loop_Before_Next_Turn()
    {
        var provider = new FakeChatProvider(
            new ChatResponse(
                Content: [new ToolUseBlock("tu_1", "search", "{}")],
                StopReason: StopReason.ToolUse,
                Usage: new UsageStats(1000, 500, 0.60m)));

        var mcp = new FakeMcpClient();
        mcp.Invocations["search"] = _ => "{}";

        var agent = new Agent(provider, mcp, options: _opts);
        var session = new AgentSession(costCapUsd: 0.50m);

        var result = await agent.RunAsync(session, "consulta");

        Assert.Equal(AgentOutcome.CostCapExceeded, result.Outcome);
        Assert.True(result.Usage.CostUsd > 0.50m);
        Assert.Single(provider.CallLog); // rodou 1 turno, custo estourou, parou antes do 2°
    }

    [Fact]
    public async Task Model_Requests_ToolUse_Without_Mcp_Returns_Error()
    {
        var provider = new FakeChatProvider(
            new ChatResponse(
                Content: [new ToolUseBlock("tu_1", "search", "{}")],
                StopReason: StopReason.ToolUse,
                Usage: new UsageStats(10, 5, 0.01m)));

        var agent = new Agent(provider, mcp: null, options: _opts);
        var session = new AgentSession();

        var result = await agent.RunAsync(session, "usa a busca");

        Assert.Equal(AgentOutcome.Error, result.Outcome);
        Assert.NotNull(result.Reason);
        Assert.Contains("IMcpClient", result.Reason);
    }

    [Fact]
    public async Task MaxSteps_Reached_When_Loop_Doesnt_Converge()
    {
        var responses = Enumerable.Range(0, 10).Select(_ =>
            new ChatResponse(
                Content: [new ToolUseBlock("tu_1", "loop", "{}")],
                StopReason: StopReason.ToolUse,
                Usage: new UsageStats(10, 5, 0.01m))).ToArray();

        var provider = new FakeChatProvider(responses);
        var mcp = new FakeMcpClient();
        mcp.Invocations["loop"] = _ => "{}";

        var opts = new AgentOptions("test-model", MaxSteps: 3);
        var agent = new Agent(provider, mcp, options: opts);
        var session = new AgentSession(costCapUsd: 100m);

        var result = await agent.RunAsync(session, "loop forever");

        Assert.Equal(AgentOutcome.MaxStepsReached, result.Outcome);
        Assert.Equal(3, result.Steps);
        Assert.Equal(3, provider.CallLog.Count);
    }

    [Fact]
    public async Task Constructor_Throws_When_Options_Missing()
    {
        var provider = new FakeChatProvider();

        Assert.Throws<ArgumentNullException>(() => new Agent(provider, options: null));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Session_Preserves_Full_Conversation_History()
    {
        var provider = new FakeChatProvider(
            new ChatResponse(
                Content: [new TextBlock("oi")],
                StopReason: StopReason.EndTurn,
                Usage: new UsageStats(10, 5, 0.01m)));

        var agent = new Agent(provider, options: _opts);
        var session = new AgentSession();

        await agent.RunAsync(session, "hello");

        Assert.Equal(2, session.Messages.Count);           // user + assistant
        Assert.Equal(ChatRole.User, session.Messages[0].Role);
        Assert.Equal(ChatRole.Assistant, session.Messages[1].Role);
    }

    /// <summary>Guardrail que só falha na fase Post — usado no teste PostTurn_Guardrail_Blocks.</summary>
    private sealed class PostOnlyGuardrail : IGuardrail
    {
        private readonly GuardrailResult _failResult;

        public PostOnlyGuardrail(string name, GuardrailResult failResult)
        {
            Name = name;
            _failResult = failResult;
        }

        public string Name { get; }

        public Task<GuardrailResult> CheckAsync(GuardrailContext context, CancellationToken ct = default) =>
            Task.FromResult(context.Stage == GuardrailStage.PostTurn
                ? _failResult
                : GuardrailResult.Pass);
    }
}
