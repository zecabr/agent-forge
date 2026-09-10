using AgentForge.Core.Chat;

namespace AgentForge.Evals;

/// <summary>Devolvido pelo <see cref="IJudge"/> — julgamento + custo do turno.</summary>
public sealed record JudgeResponse(EvalJudgment Judgment, UsageStats Usage);
