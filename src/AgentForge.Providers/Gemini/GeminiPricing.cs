namespace AgentForge.Providers.Gemini;

/// <summary>
/// Preços de referência da Gemini API (USD por 1M tokens).
/// <para>
/// Free tier via AI Studio API key (sem billing ativado) cobra $0 — a tabela
/// reflete a paid tier como upper bound. Rates de gerações 3.x são aproximadas
/// (Google não publica com detalhamento fino) — atualizar conforme confirmar.
/// </para>
/// <para>
/// Modelos 1.5/2.0 mantidos pra retro-compat com sessões antigas (mesmo já
/// descontinuados no runtime). Recomendação atual é usar os aliases
/// <c>gemini-flash-latest</c> / <c>gemini-flash-lite-latest</c> / <c>gemini-pro-latest</c>.
/// </para>
/// </summary>
public static class GeminiPricing
{
    private static readonly (string Prefix, decimal InputPerMillion, decimal OutputPerMillion)[] Rates =
    [
        // ordem importa — mais específico primeiro; -lite antes de sem lite

        // Aliases oficiais -latest (imunes a bit rot)
        ("gemini-flash-lite-latest", 0.075m, 0.30m),
        ("gemini-flash-latest",      0.10m,  0.40m),
        ("gemini-pro-latest",        1.25m,  5.00m),

        // 3.x flash family (rates aproximadas — atualizar conforme Google publicar)
        ("gemini-3.1-flash-lite",    0.075m, 0.30m),
        ("gemini-3.5-flash-lite",    0.075m, 0.30m),
        ("gemini-3.5-flash",         0.10m,  0.40m),
        ("gemini-3.6-flash",         0.10m,  0.40m),
        ("gemini-3.7-flash",         0.10m,  0.40m),
        ("gemini-3.8-flash",         0.10m,  0.40m),

        // 2.5 estáveis (jun/jul 2025)
        ("gemini-2.5-flash-lite",    0.075m, 0.30m),
        ("gemini-2.5-flash",         0.10m,  0.40m),
        ("gemini-2.5-pro",           1.25m,  5.00m),

        // Legacy 1.5/2.0 — mantidos pra retro-compat
        ("gemini-1.5-flash-8b",      0.0375m, 0.15m),
        ("gemini-1.5-flash",         0.075m,  0.30m),
        ("gemini-1.5-pro",           1.25m,   5.00m),
        ("gemini-2.0-flash-lite",    0.075m,  0.30m),
        ("gemini-2.0-flash-exp",     0m,      0m),
        ("gemini-2.0-flash",         0.10m,   0.40m),
    ];

    public static decimal Estimate(string model, int inputTokens, int outputTokens)
    {
        var rate = FindRate(model);
        if (rate is null)
        {
            return 0m;
        }

        var input = rate.Value.InputPerMillion * inputTokens / 1_000_000m;
        var output = rate.Value.OutputPerMillion * outputTokens / 1_000_000m;
        return decimal.Round(input + output, 6);
    }

    public static bool IsKnownModel(string model) => FindRate(model) is not null;

    private static (decimal InputPerMillion, decimal OutputPerMillion)? FindRate(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return null;
        }

        foreach (var (prefix, input, output) in Rates)
        {
            if (model.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return (input, output);
            }
        }

        return null;
    }
}
