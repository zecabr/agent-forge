using System.Net.Http.Headers;

namespace AgentForge.Providers.Resilience;

/// <summary>
/// <see cref="DelegatingHandler"/> que retenta requisições HTTP transientes segundo uma <see cref="RetryPolicy"/>.
/// <para>
/// Encaixe padrão: <c>new HttpClient(new RetryingHttpHandler(new HttpClientHandler(), RetryPolicy.Default))</c>.
/// Passe esse HttpClient pros providers do agent-forge e todo turno HTTP ganha retry sem código a mais.
/// </para>
/// <para>
/// Comportamento: se a resposta traz um status listado em <see cref="RetryPolicy.EffectiveStatusCodes"/>
/// e ainda há tentativas, aguarda o delay (backoff exponencial + jitter, ou <c>Retry-After</c> se presente)
/// e reenvia a request. Status finais (2xx, 4xx não-retriáveis) retornam direto sem retry.
/// Exceções de rede (<see cref="HttpRequestException"/>, <see cref="TaskCanceledException"/> por timeout do próprio HTTP)
/// também são retentadas — a exceção final propaga.
/// </para>
/// <para>
/// Respeita <see cref="Retry-After"/> quando presente (segundos ou HTTP date). Se maior que
/// <see cref="RetryPolicy.EffectiveMaxDelay"/>, aplica o cap e continua — cabe ao servidor sinalizar via 429 novamente.
/// </para>
/// </summary>
public sealed class RetryingHttpHandler : DelegatingHandler
{
    private readonly RetryPolicy _policy;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Random? _jitterSource;

    /// <summary>Construtor produção. Encadeia sobre <paramref name="inner"/> (tipicamente <see cref="HttpClientHandler"/>).</summary>
    public RetryingHttpHandler(HttpMessageHandler inner, RetryPolicy? policy = null)
        : this(inner, policy, delay: null, jitterSource: null)
    {
    }

    /// <summary>Construtor pra testes: permite substituir o <see cref="Task.Delay(TimeSpan, CancellationToken)"/> e a fonte de jitter.</summary>
    internal RetryingHttpHandler(
        HttpMessageHandler inner,
        RetryPolicy? policy,
        Func<TimeSpan, CancellationToken, Task>? delay,
        Random? jitterSource)
        : base(inner)
    {
        _policy = policy ?? RetryPolicy.Default;
        _delay = delay ?? Task.Delay;
        _jitterSource = jitterSource;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var attempt = 0;
        Exception? lastException = null;
        HttpResponseMessage? response = null;

        while (attempt < _policy.MaxAttempts)
        {
            attempt++;
            cancellationToken.ThrowIfCancellationRequested();

            // Descarta resposta anterior antes de sobrescrever (evita leak de socket em cadeia de retries).
            response?.Dispose();

            try
            {
                response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
                lastException = null;

                if (response.IsSuccessStatusCode || !_policy.IsRetriable(response.StatusCode))
                {
                    return response;
                }

                // Transient — se ainda temos tentativas, aguarda e retenta.
                if (attempt >= _policy.MaxAttempts)
                {
                    return response;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Cancelamento explícito do chamador — não retenta.
                response?.Dispose();
                throw;
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;

                if (attempt >= _policy.MaxAttempts)
                {
                    throw;
                }
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // Timeout do próprio HttpClient — trata como transiente.
                lastException = ex;

                if (attempt >= _policy.MaxAttempts)
                {
                    throw;
                }
            }

            var delay = ChooseDelay(response, attempt);
            await _delay(delay, cancellationToken).ConfigureAwait(false);
        }

        // Loop nunca sai por aqui em condição normal — o último iter ou retorna ou throw.
        // Defensivo: se caiu aqui, devolve o que temos ou joga a exceção capturada.
        if (response is not null)
        {
            return response;
        }

        throw lastException
            ?? new InvalidOperationException("RetryingHttpHandler exhausted attempts without a response or exception.");
    }

    /// <summary>
    /// Escolhe o delay pra próxima tentativa. Prefere o <c>Retry-After</c> do servidor
    /// (capado em <see cref="RetryPolicy.EffectiveMaxDelay"/>) quando presente;
    /// caso contrário aplica <see cref="RetryPolicy.ComputeDelay"/>.
    /// </summary>
    private TimeSpan ChooseDelay(HttpResponseMessage? response, int attemptNumber)
    {
        var retryAfter = ParseRetryAfter(response?.Headers.RetryAfter);
        if (retryAfter is not null)
        {
            var capped = retryAfter.Value > _policy.EffectiveMaxDelay
                ? _policy.EffectiveMaxDelay
                : retryAfter.Value;
            return capped;
        }

        return _policy.ComputeDelay(attemptNumber, _jitterSource);
    }

    private static TimeSpan? ParseRetryAfter(RetryConditionHeaderValue? header)
    {
        if (header is null)
        {
            return null;
        }

        if (header.Delta is { } delta)
        {
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        }

        if (header.Date is { } dateOffset)
        {
            var wait = dateOffset - DateTimeOffset.UtcNow;
            return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
        }

        return null;
    }
}
