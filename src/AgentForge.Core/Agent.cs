using AgentForge.Core.Abstractions;
using AgentForge.Core.Chat;
using AgentForge.Core.Guardrails;
using AgentForge.Core.Tools;

namespace AgentForge.Core;

/// <summary>
/// Orquestrador de um turno ou sessão de conversação.
/// Fluxo por iteração: guardrail pre → provider → guardrail post →
/// (se stop=ToolUse) invoca capabilities via MCP e continua; senão retorna Success.
/// </summary>
public sealed class Agent
{
    private readonly IChatProvider _provider;
    private readonly IMcpClient? _mcp;
    private readonly IReadOnlyList<IGuardrail> _guardrails;
    private readonly ITracer? _tracer;
    private readonly AgentOptions _options;

    public Agent(
        IChatProvider provider,
        IMcpClient? mcp = null,
        IEnumerable<IGuardrail>? guardrails = null,
        ITracer? tracer = null,
        AgentOptions? options = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _mcp = mcp;
        _guardrails = guardrails is null ? [] : [.. guardrails];
        _tracer = tracer;
        _options = options ?? throw new ArgumentNullException(nameof(options),
            "AgentOptions must be provided — Model has no sensible default.");
    }

    public async Task<AgentResult> RunAsync(
        AgentSession session,
        string userMessage,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        using var span = _tracer?.StartSpan("agent.run", new Dictionary<string, object?>
        {
            ["agent.session_id"] = session.Id,
            ["agent.model"] = _options.Model,
        });

        session.AppendMessage(ChatMessage.User(userMessage));

        for (int step = 1; step <= _options.MaxSteps; step++)
        {
            var pre = await CheckGuardrailsAsync(session, GuardrailStage.PreTurn, ct);
            if (!pre.Passed)
            {
                return AgentResult.Blocked(pre.Reason ?? "guardrail failed", session.CumulativeUsage, step - 1);
            }

            if (session.ExceededCostCap)
            {
                return AgentResult.CostCapExceeded(session.CumulativeUsage, step - 1);
            }

            IReadOnlyList<ToolDefinition>? tools = null;
            if (_mcp is not null)
            {
                tools = await _mcp.DiscoverToolsAsync(ct);
            }

            var request = new ChatRequest(
                Messages: session.Messages,
                Model: _options.Model,
                MaxTokens: _options.MaxTokens,
                Tools: tools);

            ChatResponse response;
            try
            {
                response = await _provider.CompleteAsync(request, ct);
            }
            catch (Exception ex)
            {
                span?.SetError(ex);
                throw;
            }

            session.RecordUsage(response.Usage);
            session.AppendMessage(ChatMessage.Assistant(response.Content));

            var post = await CheckGuardrailsAsync(session, GuardrailStage.PostTurn, ct);
            if (!post.Passed)
            {
                return AgentResult.Blocked(post.Reason ?? "guardrail failed", session.CumulativeUsage, step);
            }

            if (response.StopReason != StopReason.ToolUse)
            {
                var text = ExtractText(response.Content);
                return AgentResult.Success(text, session.CumulativeUsage, step);
            }

            var toolUses = response.Content.OfType<ToolUseBlock>().ToArray();

            if (toolUses.Length == 0)
            {
                return AgentResult.Error(
                    "Provider returned StopReason.ToolUse but no ToolUseBlock in content.",
                    session.CumulativeUsage,
                    step);
            }

            if (_mcp is null)
            {
                return AgentResult.Error(
                    "Model requested tool use but no IMcpClient was provided.",
                    session.CumulativeUsage,
                    step);
            }

            var toolResults = new List<ToolResultBlock>(toolUses.Length);
            foreach (var use in toolUses)
            {
                var result = await _mcp.InvokeAsync(use.Id, use.Name, use.InputJson, ct);
                toolResults.Add(result);
            }

            session.AppendMessage(ChatMessage.Tool(toolResults));
        }

        return AgentResult.MaxStepsReached(session.CumulativeUsage, _options.MaxSteps);
    }

    private async Task<GuardrailResult> CheckGuardrailsAsync(
        AgentSession session,
        GuardrailStage stage,
        CancellationToken ct)
    {
        if (_guardrails.Count == 0)
        {
            return GuardrailResult.Pass;
        }

        var context = new GuardrailContext(stage, session.Messages, session.CumulativeUsage.CostUsd);
        foreach (var guardrail in _guardrails)
        {
            var result = await guardrail.CheckAsync(context, ct);
            if (!result.Passed)
            {
                return result;
            }
        }

        return GuardrailResult.Pass;
    }

    private static string ExtractText(IReadOnlyList<ContentBlock> content) =>
        string.Join("\n", content.OfType<TextBlock>().Select(b => b.Text));
}
