using System.Text.RegularExpressions;
using AgentForge.Core.Abstractions;
using AgentForge.Core.Chat;
using AgentForge.Core.Guardrails;

namespace AgentForge.Guardrails;

/// <summary>
/// Detecta PII (email, CPF, CNPJ, telefone BR, SSN, cartão de crédito) na última
/// mensagem — pre-turn checa input do usuário, post-turn checa output do assistant.
/// Bloqueia quando detecta. Scrub (redação in-place) fica pra v0.2, quando o
/// contrato do IGuardrail permitir mutação de mensagens.
/// </summary>
public sealed class PiiDetectionGuardrail : IGuardrail
{
    /// <summary>Kinds suportados por default — pode restringir via construtor.</summary>
    public static IReadOnlyList<string> AllKinds { get; } =
        ["email", "cpf", "cnpj", "phone-br", "ssn", "credit-card"];

    private static readonly (string Kind, Regex Pattern)[] AllPatterns =
    [
        ("email",       new Regex(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("cpf",         new Regex(@"\b\d{3}\.\d{3}\.\d{3}-\d{2}\b", RegexOptions.Compiled)),
        ("cnpj",        new Regex(@"\b\d{2}\.\d{3}\.\d{3}/\d{4}-\d{2}\b", RegexOptions.Compiled)),
        ("phone-br",    new Regex(@"\(?[1-9]{2}\)?\s?9?\d{4}[-\s]?\d{4}", RegexOptions.Compiled)),
        ("ssn",         new Regex(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled)),
        ("credit-card", new Regex(@"\b(?:\d{4}[\s-]?){3}\d{4}\b", RegexOptions.Compiled)),
    ];

    private readonly HashSet<string> _enabled;

    public PiiDetectionGuardrail(IEnumerable<string>? kindsToDetect = null)
    {
        _enabled = kindsToDetect is null
            ? [.. AllKinds]
            : [.. kindsToDetect];
    }

    public string Name => "pii-detection";

    public Task<GuardrailResult> CheckAsync(GuardrailContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var role = context.Stage == GuardrailStage.PreTurn ? ChatRole.User : ChatRole.Assistant;
        var msg = context.Messages.LastOrDefault(m => m.Role == role);
        if (msg is null)
        {
            return Task.FromResult(GuardrailResult.Pass);
        }

        var text = string.Join(" ", msg.Content.OfType<TextBlock>().Select(b => b.Text));
        var detected = new List<string>();

        foreach (var (kind, pattern) in AllPatterns)
        {
            if (!_enabled.Contains(kind))
            {
                continue;
            }

            if (pattern.IsMatch(text))
            {
                detected.Add(kind);
            }
        }

        if (detected.Count == 0)
        {
            return Task.FromResult(GuardrailResult.Pass);
        }

        return Task.FromResult(GuardrailResult.Fail(
            $"PII detected in {context.Stage} message: {string.Join(", ", detected)}"));
    }
}
