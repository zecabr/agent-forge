namespace AgentForge.Core;

/// <summary>
/// Configuração de um <see cref="Agent"/>. Model é obrigatório —
/// não há default sensato (cada provider tem seu catálogo).
/// </summary>
public sealed record AgentOptions(
    string Model,
    int MaxTokens = 4096,
    int MaxSteps = 10);
