using System.Net;

namespace AgentForge.Providers.Resilience;

/// <summary>
/// Política de retry pra falhas HTTP transientes. Imutável, thread-safe.
/// Consumida pelo <see cref="RetryingHttpHandler"/>.
/// </summary>
/// <param name="MaxAttempts">
/// Total de tentativas incluindo a primeira. <c>3</c> = tentativa inicial + 2 retries.
/// Valor mínimo 1 (sem retry).
/// </param>
/// <param name="InitialDelay">
/// Espera antes do primeiro retry. Padrão 100ms. As esperas seguintes crescem
/// exponencialmente por <see cref="BackoffMultiplier"/>, capadas em <see cref="MaxDelay"/>.
/// </param>
/// <param name="BackoffMultiplier">
/// Multiplicador do backoff exponencial. Padrão 2.0 (dobra a cada tentativa).
/// </param>
/// <param name="MaxDelay">
/// Teto duro pra qualquer espera. Padrão 30s. Impede backoff exponencial
/// escalar pra minutos numa cadeia longa de falhas.
/// </param>
/// <param name="JitterFactor">
/// Fração aleatória adicionada ao delay pra desincronizar clients em falha
/// simultânea (thundering herd). <c>0.2</c> = ±20% do delay calculado.
/// Zero desliga.
/// </param>
/// <param name="RetriableStatusCodes">
/// Status codes que disparam retry. Default cobre:
/// 408 (Request Timeout), 425 (Too Early), 429 (Rate Limit),
/// 500 (Internal Server Error), 502 (Bad Gateway),
/// 503 (Service Unavailable), 504 (Gateway Timeout).
/// </param>
public sealed record RetryPolicy(
    int MaxAttempts = 3,
    TimeSpan? InitialDelay = null,
    double BackoffMultiplier = 2.0,
    TimeSpan? MaxDelay = null,
    double JitterFactor = 0.2,
    IReadOnlySet<HttpStatusCode>? RetriableStatusCodes = null)
{
    /// <summary>Política default: 3 tentativas, backoff exponencial 100ms→200ms→400ms com jitter ±20%.</summary>
    public static RetryPolicy Default { get; } = new();

    /// <summary>Sem retry — útil pra testes que querem falha imediata.</summary>
    public static RetryPolicy Disabled { get; } = new(MaxAttempts: 1);

    private static readonly IReadOnlySet<HttpStatusCode> DefaultCodes = new HashSet<HttpStatusCode>
    {
        HttpStatusCode.RequestTimeout,       // 408
        (HttpStatusCode)425,                  // 425 Too Early — não tem membro no enum até .NET 9
        HttpStatusCode.TooManyRequests,      // 429
        HttpStatusCode.InternalServerError,  // 500
        HttpStatusCode.BadGateway,           // 502
        HttpStatusCode.ServiceUnavailable,   // 503
        HttpStatusCode.GatewayTimeout,       // 504
    };

    /// <summary>Status codes que disparam retry. Default se não configurado.</summary>
    public IReadOnlySet<HttpStatusCode> EffectiveStatusCodes => RetriableStatusCodes ?? DefaultCodes;

    /// <summary>Espera antes do primeiro retry.</summary>
    public TimeSpan EffectiveInitialDelay => InitialDelay ?? TimeSpan.FromMilliseconds(100);

    /// <summary>Teto de espera.</summary>
    public TimeSpan EffectiveMaxDelay => MaxDelay ?? TimeSpan.FromSeconds(30);

    /// <summary>
    /// Retorna verdadeiro se o status merece retry por essa política.
    /// </summary>
    public bool IsRetriable(HttpStatusCode status) => EffectiveStatusCodes.Contains(status);

    /// <summary>
    /// Calcula o delay pra tentativa <paramref name="attemptNumber"/> (1-based: 1 = primeiro retry).
    /// Aplica backoff exponencial, jitter e cap em <see cref="MaxDelay"/>.
    /// </summary>
    /// <param name="attemptNumber">Número da tentativa de retry (1 = primeiro retry após a tentativa inicial).</param>
    /// <param name="jitterSource">Random pra jitter; injeta pra testes determinísticos.</param>
    public TimeSpan ComputeDelay(int attemptNumber, Random? jitterSource = null)
    {
        if (attemptNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptNumber), "Attempt number must be >= 1.");
        }

        var baseMs = EffectiveInitialDelay.TotalMilliseconds * Math.Pow(BackoffMultiplier, attemptNumber - 1);
        var maxMs = EffectiveMaxDelay.TotalMilliseconds;
        var cappedMs = Math.Min(baseMs, maxMs);

        if (JitterFactor > 0)
        {
            var rnd = jitterSource ?? Random.Shared;
            // jitter em ±JitterFactor: multiplica por (1 + [-J, +J])
            var jitter = (rnd.NextDouble() * 2 - 1) * JitterFactor;
            cappedMs = Math.Max(0, cappedMs * (1 + jitter));
        }

        return TimeSpan.FromMilliseconds(cappedMs);
    }
}
