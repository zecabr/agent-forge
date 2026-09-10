using AgentForge.Core.Tools;

namespace AgentForge.Core.Chat;

/// <summary>
/// Requisição a um provider de LLM. Modelo é obrigatório —
/// cada provider valida seu próprio catálogo.
/// </summary>
public sealed record ChatRequest(
    IReadOnlyList<ChatMessage> Messages,
    string Model,
    int MaxTokens = 4096,
    IReadOnlyList<ToolDefinition>? Tools = null,
    double? Temperature = null,
    IReadOnlyList<string>? StopSequences = null);
