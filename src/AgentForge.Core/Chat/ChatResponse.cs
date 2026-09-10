namespace AgentForge.Core.Chat;

/// <summary>
/// Resposta de um provider de LLM. Content pode ter texto e/ou tool_uses.
/// </summary>
public sealed record ChatResponse(
    IReadOnlyList<ContentBlock> Content,
    StopReason StopReason,
    UsageStats Usage,
    string? Model = null);

public enum StopReason
{
    /// <summary>Modelo terminou o turno naturalmente.</summary>
    EndTurn,

    /// <summary>Bateu o limite de tokens.</summary>
    MaxTokens,

    /// <summary>Modelo pediu uma tool — loop deve invocá-la e continuar.</summary>
    ToolUse,

    /// <summary>Bateu uma stop sequence configurada.</summary>
    StopSequence,
}
