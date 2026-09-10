namespace AgentForge.Providers.Anthropic;

/// <summary>
/// Preços de referência da API Anthropic (USD por 1M tokens).
/// Modelo desconhecido devolve custo zero — o consumidor decide se usa outro mecanismo.
/// Atualizar conforme a Anthropic anuncia novos modelos ou muda preços.
/// </summary>
public static class AnthropicPricing
{
    private static readonly Dictionary<string, (decimal InputPerMillion, decimal OutputPerMillion)> Rates =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude-3-5-sonnet-20241022"] = (3.00m, 15.00m),
        ["claude-3-5-sonnet-latest"]   = (3.00m, 15.00m),
        ["claude-3-5-haiku-20241022"]  = (0.80m, 4.00m),
        ["claude-3-5-haiku-latest"]    = (0.80m, 4.00m),
        ["claude-3-opus-20240229"]     = (15.00m, 75.00m),
        ["claude-3-opus-latest"]       = (15.00m, 75.00m),
    };

    public static decimal Estimate(string model, int inputTokens, int outputTokens)
    {
        if (!Rates.TryGetValue(model, out var rates))
        {
            return 0m;
        }

        var inputCost = rates.InputPerMillion * inputTokens / 1_000_000m;
        var outputCost = rates.OutputPerMillion * outputTokens / 1_000_000m;
        return decimal.Round(inputCost + outputCost, 6);
    }

    public static bool IsKnownModel(string model) => Rates.ContainsKey(model);
}
