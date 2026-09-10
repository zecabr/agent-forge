using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AgentForge.Core.Abstractions;
using AgentForge.Core.Chat;

namespace AgentForge.Evals;

/// <summary>
/// Julgador padrão: pede a um LLM que compare a resposta do agente com o critério
/// e devolva veredito estruturado (JSON). Uso típico: mesmo provider Anthropic
/// do agente sendo avaliado, com modelo mais forte (ex.: Opus julgando Sonnet).
/// </summary>
public sealed class LlmJudge : IJudge
{
    private const string SystemPrompt = """
        Você é um avaliador estrito de respostas de assistentes de IA.
        Recebe: pergunta original do usuário, resposta que o assistente deu, critério objetivo.
        Sua tarefa é decidir se a resposta cumpre o critério.

        Responda SEMPRE em JSON com este formato exato:
        {"passed": true|false, "reason": "explicação em 1 frase"}

        NÃO adicione texto antes ou depois do JSON. NÃO use markdown.
        """;

    private readonly IChatProvider _provider;
    private readonly string _model;

    public LlmJudge(IChatProvider provider, string model)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model must not be empty.", nameof(model));
        }

        _model = model;
    }

    public async Task<JudgeResponse> JudgeAsync(
        string userPrompt,
        string agentResponse,
        string criterion,
        CancellationToken ct = default)
    {
        var body = $$"""
            <pergunta_original>
            {{userPrompt}}
            </pergunta_original>

            <resposta_do_assistente>
            {{agentResponse}}
            </resposta_do_assistente>

            <criterio>
            {{criterion}}
            </criterio>

            Avalie e responda em JSON.
            """;

        var request = new ChatRequest(
            Messages: [
                ChatMessage.System(SystemPrompt),
                ChatMessage.User(body),
            ],
            Model: _model,
            MaxTokens: 512,
            Temperature: 0.0);

        var response = await _provider.CompleteAsync(request, ct).ConfigureAwait(false);

        var text = string.Concat(response.Content.OfType<TextBlock>().Select(b => b.Text)).Trim();
        var judgment = ParseJudgment(text);

        return new JudgeResponse(judgment, response.Usage);
    }

    /// <summary>Extrai JSON do texto do julgador, tolerando envelopes markdown.</summary>
    internal static EvalJudgment ParseJudgment(string text)
    {
        var trimmed = text.Trim();

        // remove markdown code fence ```json ... ``` se presente
        var fenceMatch = Regex.Match(trimmed, @"^```(?:json)?\s*(?<body>.+?)\s*```$", RegexOptions.Singleline);
        if (fenceMatch.Success)
        {
            trimmed = fenceMatch.Groups["body"].Value.Trim();
        }

        try
        {
            var node = JsonNode.Parse(trimmed);
            if (node is null)
            {
                return new EvalJudgment(false, "judge returned empty response");
            }

            var passed = node["passed"]?.GetValue<bool>() ?? false;
            var reason = node["reason"]?.GetValue<string>() ?? "no reason provided";
            return new EvalJudgment(passed, reason);
        }
        catch (Exception ex)
        {
            return new EvalJudgment(false, $"judge returned invalid JSON: {ex.Message}");
        }
    }
}
