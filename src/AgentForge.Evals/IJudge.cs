namespace AgentForge.Evals;

/// <summary>
/// Julgador de qualidade de resposta. A implementação padrão (<see cref="LlmJudge"/>)
/// usa um LLM; testes podem trocar por qualquer implementação (fixed pass/fail, keyword match).
/// </summary>
public interface IJudge
{
    Task<JudgeResponse> JudgeAsync(
        string userPrompt,
        string agentResponse,
        string criterion,
        CancellationToken ct = default);
}
