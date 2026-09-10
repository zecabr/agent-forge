using System.Text.RegularExpressions;
using AgentForge.Core.Abstractions;
using AgentForge.Core.Chat;
using AgentForge.Core.Guardrails;

namespace AgentForge.Guardrails;

/// <summary>
/// Filtro baseline de prompt injection — cobre os padrões mais comuns
/// em inglês e português. Roda só em <see cref="GuardrailStage.PreTurn"/>
/// na última mensagem do usuário. Substitua por modelo classificador
/// (LLM-as-judge) em produção quando o custo justificar.
/// </summary>
public sealed class PromptInjectionGuardrail : IGuardrail
{
    private static readonly Regex[] Patterns =
    [
        new(@"ignore\s+(?:all\s+)?(?:previous|prior)\s+instructions", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"disregard\s+(?:the\s+)?(?:above|previous)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"you\s+are\s+now\s+(?:a|an|the)\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"system\s+prompt\s+is\s+now", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"forget\s+everything\s+(?:above|before)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"ignore\s+(?:as\s+)?instru[cç][õo]es\s+(?:anteriores|acima)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"desconsidere\s+(?:as\s+)?instru[cç][õo]es", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"esque[cç]a\s+tudo\s+(?:acima|antes)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"agora\s+voc[êe]\s+[eé]\s+(?:um|uma|o|a)\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    public string Name => "prompt-injection";

    public Task<GuardrailResult> CheckAsync(GuardrailContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Stage != GuardrailStage.PreTurn)
        {
            return Task.FromResult(GuardrailResult.Pass);
        }

        var lastUser = context.Messages.LastOrDefault(m => m.Role == ChatRole.User);
        if (lastUser is null)
        {
            return Task.FromResult(GuardrailResult.Pass);
        }

        var text = string.Join(" ", lastUser.Content.OfType<TextBlock>().Select(b => b.Text));

        foreach (var pattern in Patterns)
        {
            var match = pattern.Match(text);
            if (match.Success)
            {
                return Task.FromResult(GuardrailResult.Fail(
                    $"prompt-injection pattern detected: '{match.Value}'"));
            }
        }

        return Task.FromResult(GuardrailResult.Pass);
    }
}
