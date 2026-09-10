using AgentForge.Core.Chat;
using AgentForge.Core.Guardrails;

namespace AgentForge.Core.Abstractions;

/// <summary>
/// Verifica uma mensagem/estado antes ou depois de um turno.
/// Falha em qualquer guardrail interrompe o loop (ver ADR-002).
/// </summary>
public interface IGuardrail
{
    string Name { get; }

    Task<GuardrailResult> CheckAsync(GuardrailContext context, CancellationToken ct = default);
}

public sealed record GuardrailContext(
    GuardrailStage Stage,
    IReadOnlyList<ChatMessage> Messages,
    decimal CumulativeCostUsd);

public enum GuardrailStage
{
    /// <summary>Antes de enviar mensagens pro provider.</summary>
    PreTurn,

    /// <summary>Depois de receber resposta do provider.</summary>
    PostTurn,
}
