using System.Net;
using AgentForge.Providers.Resilience;
using Xunit;

namespace AgentForge.Providers.Tests.Resilience;

public class RetryPolicyTests
{
    [Fact]
    public void Default_Has_Sensible_Values()
    {
        var p = RetryPolicy.Default;

        Assert.Equal(3, p.MaxAttempts);
        Assert.Equal(TimeSpan.FromMilliseconds(100), p.EffectiveInitialDelay);
        Assert.Equal(2.0, p.BackoffMultiplier);
        Assert.Equal(TimeSpan.FromSeconds(30), p.EffectiveMaxDelay);
        Assert.Equal(0.2, p.JitterFactor);
    }

    [Fact]
    public void Disabled_Has_One_Attempt()
    {
        Assert.Equal(1, RetryPolicy.Disabled.MaxAttempts);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.BadGateway, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.GatewayTimeout, true)]
    [InlineData(HttpStatusCode.OK, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public void Default_Retriable_Set_Includes_Transient_5xx_And_429(HttpStatusCode code, bool expected)
    {
        Assert.Equal(expected, RetryPolicy.Default.IsRetriable(code));
    }

    [Fact]
    public void HTTP_425_Too_Early_Is_Retriable_By_Default()
    {
        Assert.True(RetryPolicy.Default.IsRetriable((HttpStatusCode)425));
    }

    [Fact]
    public void Custom_Retriable_Set_Overrides_Default()
    {
        // 418 I'm a Teapot — não existe no enum HttpStatusCode até .NET 9,
        // então cast explícito. Escolha inofensiva pra provar que o set custom é honrado.
        var teapot = (HttpStatusCode)418;
        var p = new RetryPolicy(
            RetriableStatusCodes: new HashSet<HttpStatusCode> { teapot });

        Assert.True(p.IsRetriable(teapot));
        Assert.False(p.IsRetriable(HttpStatusCode.ServiceUnavailable));
    }

    [Fact]
    public void ComputeDelay_Without_Jitter_Grows_Exponentially()
    {
        var p = new RetryPolicy(JitterFactor: 0);

        Assert.Equal(TimeSpan.FromMilliseconds(100), p.ComputeDelay(1));
        Assert.Equal(TimeSpan.FromMilliseconds(200), p.ComputeDelay(2));
        Assert.Equal(TimeSpan.FromMilliseconds(400), p.ComputeDelay(3));
        Assert.Equal(TimeSpan.FromMilliseconds(800), p.ComputeDelay(4));
    }

    [Fact]
    public void ComputeDelay_Caps_At_MaxDelay()
    {
        var p = new RetryPolicy(
            InitialDelay: TimeSpan.FromSeconds(1),
            BackoffMultiplier: 10.0,
            MaxDelay: TimeSpan.FromSeconds(5),
            JitterFactor: 0);

        Assert.Equal(TimeSpan.FromSeconds(1), p.ComputeDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(5), p.ComputeDelay(2)); // 10s capped
        Assert.Equal(TimeSpan.FromSeconds(5), p.ComputeDelay(3)); // 100s capped
    }

    [Fact]
    public void ComputeDelay_With_Jitter_Stays_Within_Band()
    {
        var p = new RetryPolicy(JitterFactor: 0.2);
        var rnd = new Random(42);

        for (var i = 0; i < 100; i++)
        {
            var delay = p.ComputeDelay(2, rnd);
            // Base = 200ms, jitter ±20% = [160ms, 240ms]
            Assert.InRange(delay.TotalMilliseconds, 160, 240);
        }
    }

    [Fact]
    public void ComputeDelay_Rejects_Zero_Or_Negative_Attempt()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RetryPolicy.Default.ComputeDelay(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => RetryPolicy.Default.ComputeDelay(-1));
    }
}
