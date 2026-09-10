namespace AgentForge.Evals;

/// <summary>
/// Um caso de avaliação: pergunta pro agente + critério que a resposta precisa cumprir.
/// </summary>
public sealed record EvalCase(
    string Name,
    string UserPrompt,
    string Criterion,
    string? Description = null);
