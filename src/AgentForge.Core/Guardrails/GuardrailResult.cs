namespace AgentForge.Core.Guardrails;

/// <summary>
/// Resultado de um guardrail. Falha em qualquer guardrail interrompe o turno —
/// o consumidor decide se tenta de novo ou reporta erro.
/// </summary>
public sealed record GuardrailResult(bool Passed, string? Reason = null)
{
    public static GuardrailResult Pass { get; } = new(true);

    public static GuardrailResult Fail(string reason) => new(false, reason);
}
