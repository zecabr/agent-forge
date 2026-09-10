namespace AgentForge.Evals;

/// <summary>Resultado do julgamento — passou ou não, com razão em 1 frase.</summary>
public sealed record EvalJudgment(bool Passed, string Reason);
