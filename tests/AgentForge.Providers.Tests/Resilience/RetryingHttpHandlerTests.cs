using System.Net;
using System.Net.Http.Headers;
using AgentForge.Providers.Resilience;
using AgentForge.Providers.Tests.Fakes;
using Xunit;

namespace AgentForge.Providers.Tests.Resilience;

public class RetryingHttpHandlerTests
{
    private const string Body = "{\"ok\":true}";
    private static readonly Uri Endpoint = new("https://example.test/api");

    private static RetryingHttpHandler NewHandler(
        HttpMessageHandler inner,
        RetryPolicy? policy = null,
        List<TimeSpan>? recordedDelays = null,
        Random? jitterSource = null)
    {
        Func<TimeSpan, CancellationToken, Task>? delay = recordedDelays is null
            ? (_, _) => Task.CompletedTask
            : (ts, _) => { recordedDelays.Add(ts); return Task.CompletedTask; };

        return new RetryingHttpHandler(inner, policy, delay, jitterSource);
    }

    private static Task<HttpResponseMessage> Send(RetryingHttpHandler handler, CancellationToken ct = default)
    {
        using var client = new HttpClient(handler, disposeHandler: false);
        return client.SendAsync(new HttpRequestMessage(HttpMethod.Post, Endpoint), ct);
    }

    [Fact]
    public async Task Success_On_First_Attempt_Does_Not_Retry()
    {
        var inner = new RecordingHandler(HttpStatusCode.OK, Body);
        using var handler = NewHandler(inner);

        var response = await Send(handler);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(inner.Calls);
    }

    [Fact]
    public async Task Retries_Once_On_503_Then_Succeeds()
    {
        var inner = new RecordingHandler(
            (HttpStatusCode.ServiceUnavailable, "boom"),
            (HttpStatusCode.OK, Body));
        var delays = new List<TimeSpan>();
        using var handler = NewHandler(inner, recordedDelays: delays);

        var response = await Send(handler);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Calls.Count);
        Assert.Single(delays);
    }

    [Fact]
    public async Task Retries_Up_To_MaxAttempts_And_Returns_Last_Response()
    {
        var inner = new RecordingHandler(
            (HttpStatusCode.ServiceUnavailable, "boom 1"),
            (HttpStatusCode.ServiceUnavailable, "boom 2"),
            (HttpStatusCode.ServiceUnavailable, "boom 3"));
        var delays = new List<TimeSpan>();
        using var handler = NewHandler(inner, recordedDelays: delays);

        var response = await Send(handler);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(3, inner.Calls.Count);
        Assert.Equal(2, delays.Count); // 3 attempts = 2 waits between them
    }

    [Fact]
    public async Task Non_Retriable_Status_Returns_Immediately()
    {
        var inner = new RecordingHandler(HttpStatusCode.BadRequest, "bad");
        using var handler = NewHandler(inner);

        var response = await Send(handler);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single(inner.Calls);
    }

    [Fact]
    public async Task Retries_On_HttpRequestException_And_Succeeds()
    {
        var inner = new ThrowingThenSucceedingHandler(HttpStatusCode.OK, Body,
            throwOnCallsBeforeSuccess: 1);
        var delays = new List<TimeSpan>();
        using var handler = NewHandler(inner, recordedDelays: delays);

        var response = await Send(handler);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Calls);
        Assert.Single(delays);
    }

    [Fact]
    public async Task HttpRequestException_Propagates_After_MaxAttempts()
    {
        // Sempre throw — max attempts = 3, então lança na 3ª e joga pra cima.
        var inner = new ThrowingThenSucceedingHandler(HttpStatusCode.OK, Body,
            throwOnCallsBeforeSuccess: 99);
        var delays = new List<TimeSpan>();
        using var handler = NewHandler(inner, recordedDelays: delays);

        await Assert.ThrowsAsync<HttpRequestException>(() => Send(handler));

        Assert.Equal(3, inner.Calls);
        Assert.Equal(2, delays.Count);
    }

    [Fact]
    public async Task Retry_After_Header_In_Seconds_Overrides_Backoff()
    {
        var response503 = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        response503.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));

        var inner = new StaticResponseHandler(response503, new HttpResponseMessage(HttpStatusCode.OK));
        var delays = new List<TimeSpan>();
        using var handler = NewHandler(inner, recordedDelays: delays);

        var response = await Send(handler);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(delays);
        Assert.Equal(TimeSpan.FromSeconds(7), delays[0]);
    }

    [Fact]
    public async Task Retry_After_Header_Capped_At_MaxDelay()
    {
        var response503 = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        response503.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(10));

        var inner = new StaticResponseHandler(response503, new HttpResponseMessage(HttpStatusCode.OK));
        var delays = new List<TimeSpan>();
        var policy = new RetryPolicy(MaxDelay: TimeSpan.FromSeconds(5));
        using var handler = NewHandler(inner, policy, delays);

        _ = await Send(handler);

        Assert.Equal(TimeSpan.FromSeconds(5), delays[0]);
    }

    [Fact]
    public async Task Cancellation_During_Send_Propagates_Without_Retry()
    {
        using var cts = new CancellationTokenSource();
        var inner = new CancellingHandler(cts);
        using var handler = NewHandler(inner);

        await Assert.ThrowsAsync<TaskCanceledException>(() => Send(handler, cts.Token));

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task Zero_MaxAttempts_Is_Treated_As_One_Attempt_By_Contract()
    {
        // MaxAttempts=1 é Disabled. Não deve retentar mesmo em 503.
        var inner = new RecordingHandler(HttpStatusCode.ServiceUnavailable, "boom");
        using var handler = NewHandler(inner, RetryPolicy.Disabled);

        var response = await Send(handler);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Single(inner.Calls);
    }
}

/// <summary>Handler que joga HttpRequestException por N chamadas e depois responde ok.</summary>
file sealed class ThrowingThenSucceedingHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _finalStatus;
    private readonly string _finalBody;
    private readonly int _throwsBefore;
    private int _calls;

    public ThrowingThenSucceedingHandler(HttpStatusCode finalStatus, string finalBody, int throwOnCallsBeforeSuccess)
    {
        _finalStatus = finalStatus;
        _finalBody = finalBody;
        _throwsBefore = throwOnCallsBeforeSuccess;
    }

    public int Calls => _calls;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        _calls++;
        if (_calls <= _throwsBefore)
        {
            throw new HttpRequestException("simulated transient network error");
        }

        return Task.FromResult(new HttpResponseMessage(_finalStatus)
        {
            Content = new StringContent(_finalBody),
        });
    }
}

/// <summary>Handler que responde com respostas pré-programadas em ordem.</summary>
file sealed class StaticResponseHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses;

    public StaticResponseHandler(params HttpResponseMessage[] responses)
    {
        _responses = new Queue<HttpResponseMessage>(responses);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        return Task.FromResult(_responses.Dequeue());
    }
}

/// <summary>Handler que cancela o token na primeira chamada e joga.</summary>
file sealed class CancellingHandler : HttpMessageHandler
{
    private readonly CancellationTokenSource _cts;
    private int _calls;

    public CancellingHandler(CancellationTokenSource cts)
    {
        _cts = cts;
    }

    public int Calls => _calls;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        _calls++;
        _cts.Cancel();
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
